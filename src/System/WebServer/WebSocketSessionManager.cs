using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Buffers;
using System.Diagnostics;

namespace LiteMonitor.src.WebServer
{
    public class WebSocketSessionManager
    {
        private readonly ConcurrentDictionary<TcpClient, bool> _wsClients = new();
        // [Optimization] Use Action<Utf8JsonWriter> for zero-allocation JSON generation
        private readonly Action<Utf8JsonWriter> _dataProvider;
        private volatile bool _isRunning = false;

        public WebSocketSessionManager(Action<Utf8JsonWriter> dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;
            Task.Run(BroadcastLoop);
        }

        public void Stop()
        {
            _isRunning = false;
            foreach (var client in _wsClients.Keys)
            {
                try { client.Close(); } catch { }
            }
            _wsClients.Clear();
        }

        /// <summary>
        /// 尝试处理 WebSocket 握手。如果成功，该连接将由 SessionManager 接管。
        /// </summary>
        public bool TryHandleHandshake(TcpClient client, string requestStr)
        {
            try
            {
                var stream = client.GetStream();
                if (Handshake(stream, requestStr))
                {
                    // 握手成功，加入客户端列表
                    _wsClients.TryAdd(client, true);

                    // 启动接收循环以处理 Ping/Close 和排空缓冲区
                    // 这里的 client 由 ReceiveLoop 负责在断开时关闭
                    _ = Task.Run(() => ReceiveLoop(client));
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WebSocket Handshake Failed: {ex.Message}");
            }
            return false;
        }

        private async Task BroadcastLoop()
        {
            while (_isRunning)
            {
                // [Optimization] 如果没有任何客户端连接，降低循环频率以节省资源
                if (_wsClients.IsEmpty)
                {
                    await Task.Delay(2000);
                    continue;
                }

                byte[] buffer = null;
                try
                {
                    // [Optimization] Rent a reasonable buffer size (8KB is usually enough for JSON)
                    // If JSON exceeds 8KB, Utf8JsonWriter works best with IBufferWriter, but for simplicity with ArrayPool:
                    // We'll stick to a fixed size that covers 99% cases. 32KB is safer if you have many plugins.
                    buffer = ArrayPool<byte>.Shared.Rent(32 * 1024); 
                    
                    int payloadLen;
                    using (var ms = new System.IO.MemoryStream(buffer))
                    using (var writer = new Utf8JsonWriter(ms))
                    {
                        _dataProvider(writer);
                        writer.Flush();
                        payloadLen = (int)ms.Position;
                    }

                    // [Optimization] Fast-Path: If no clients, return immediately (should be handled by IsEmpty check above)
                    
                    // Encode Header
                    // We can reuse the same buffer logic or just create a small header buffer.
                    // To minimize "Big Buffer" holding time during network IO, we should:
                    // 1. Copy data to a perfectly-sized buffer for network transmission? 
                    //    No, that allocates memory (creating new byte[]).
                    // 2. Or just send from the large buffer? 
                    //    If we send from large buffer, we hold it for the duration of network IO.
                    //    If network is slow, ArrayPool will grow.
                    
                    // Best Balance: 
                    // Since we already use `SendFrameSafeAsync` which does `stream.WriteAsync`, 
                    // it copies data to kernel socket buffer quickly unless socket buffer is full.
                    // But to avoid locking the 32KB buffer for slow clients, let's Copy to a specialized frame buffer
                    // exactly sized to the payload.
                    
                    // Wait! Allocation of exact-sized buffer defeats the purpose of ArrayPool?
                    // No. We use ArrayPool for the *scratchpad* (JSON generation).
                    // For sending, if we want to release the scratchpad immediately:
                    
                    int headerLen = 2;
                    if (payloadLen >= 65536) headerLen += 8;
                    else if (payloadLen >= 126) headerLen += 2;
                    int totalLen = headerLen + payloadLen;

                    // Rent a target buffer for the frame
                    byte[] frameBuffer = ArrayPool<byte>.Shared.Rent(totalLen);
                    try
                    {
                        int payloadOffset = EncodeFrameHeader(payloadLen, frameBuffer);
                        Buffer.BlockCopy(buffer, 0, frameBuffer, payloadOffset, payloadLen);

                        // Now we can return the "JSON Scratchpad" buffer IMMEDIATELY!
                        ArrayPool<byte>.Shared.Return(buffer);
                        buffer = null; // Prevent double return in finally

                        // Broadcast using frameBuffer
                        var tasks = new List<Task>();
                        // Capture the buffer and length for the closure
                        var bufToSend = frameBuffer; 
                        var lenToSend = totalLen;

                        foreach (var client in _wsClients.Keys)
                        {
                            if (!client.Connected)
                            {
                                _wsClients.TryRemove(client, out _);
                                continue;
                            }
                            // We must NOT return frameBuffer until ALL sends are done.
                            // But since we await Task.WhenAll, it's fine.
                            tasks.Add(SendFrameSafeAsync(client, bufToSend, lenToSend));
                        }

                        if (tasks.Count > 0)
                        {
                            await Task.WhenAll(tasks);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(frameBuffer);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Broadcast Error: {ex.Message}");
                }
                finally
                {
                    if (buffer != null) ArrayPool<byte>.Shared.Return(buffer);
                }

                // 正常广播频率：每秒一次
                await Task.Delay(1000);
            }
        }

        private int EncodeFrameHeader(int payloadLen, byte[] buffer)
        {
            int offset = 0;

            // FIN(1) + RSV(000) + OpCode(1=Text) => 10000001 => 0x81
            buffer[offset++] = 0x81;

            // Mask(0) + Length
            if (payloadLen < 126)
            {
                buffer[offset++] = (byte)payloadLen;
            }
            else if (payloadLen <= 65535)
            {
                buffer[offset++] = 126;
                buffer[offset++] = (byte)((payloadLen >> 8) & 0xFF);
                buffer[offset++] = (byte)(payloadLen & 0xFF);
            }
            else
            {
                buffer[offset++] = 127;
                // 64-bit length
                buffer[offset++] = 0; buffer[offset++] = 0; buffer[offset++] = 0; buffer[offset++] = 0;
                buffer[offset++] = (byte)((payloadLen >> 24) & 0xFF);
                buffer[offset++] = (byte)((payloadLen >> 16) & 0xFF);
                buffer[offset++] = (byte)((payloadLen >> 8) & 0xFF);
                buffer[offset++] = (byte)(payloadLen & 0xFF);
            }
            return offset;
        }

        private async Task SendFrameSafeAsync(TcpClient client, byte[] buffer, int length)
        {
            try
            {
                var stream = client.GetStream();
                // [Fix] Add timeout to prevent memory leak from stuck tasks
                using var cts = new CancellationTokenSource(2000); // 2s timeout
                await stream.WriteAsync(buffer, 0, length, cts.Token);
            }
            catch
            {
                // Remove dead client
                _wsClients.TryRemove(client, out _);
                try { client.Close(); } catch { }
            }
        }


        private async Task ReceiveLoop(TcpClient client)
        {
            byte[] buffer = null;
            try
            {
                var stream = client.GetStream();
                // [Optimization] Use ArrayPool for receive buffer
                buffer = ArrayPool<byte>.Shared.Rent(1024);

                while (client.Connected && _wsClients.ContainsKey(client))
                {
                    // 阻塞读取，如果断开会抛出异常或返回0
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break; // 客户端关闭连接

                    // 解析帧头判断是否为 Close 帧 (OpCode = 8)
                    // 0x88 = 1000 1000 = FIN + Close
                    if ((buffer[0] & 0x0F) == 0x08)
                    {
                        break; // 收到关闭帧
                    }
                }
            }
            catch { }
            finally
            {
                if (buffer != null) ArrayPool<byte>.Shared.Return(buffer);
                _wsClients.TryRemove(client, out _);
                try { client.Close(); } catch { }
            }
        }

        private bool Handshake(NetworkStream stream, string request)
        {
            try
            {
                // 1. 提取 Sec-WebSocket-Key
                var match = Regex.Match(request, "Sec-WebSocket-Key: (.*)");
                if (!match.Success) return false;

                string key = match.Groups[1].Value.Trim();

                // 2. 生成 Accept Key (Magic String: 258EAFA5-E914-47DA-95CA-C5AB0DC85B11)
                string magic = key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
                byte[] hash = SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(magic));
                string acceptKey = Convert.ToBase64String(hash);

                // 3. 发送握手响应
                var response = new StringBuilder();
                response.Append("HTTP/1.1 101 Switching Protocols\r\n");
                response.Append("Connection: Upgrade\r\n");
                response.Append("Upgrade: websocket\r\n");
                response.Append($"Sec-WebSocket-Accept: {acceptKey}\r\n");
                response.Append("\r\n");

                byte[] respBytes = Encoding.UTF8.GetBytes(response.ToString());
                stream.Write(respBytes, 0, respBytes.Length);
                return true;
            }
            catch { return false; }
        }
    }
}
