using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json; // [Fix] Added missing using
using System.Buffers; // [Optimization] Use ArrayPool
using LiteMonitor.src.Core;
using LiteMonitor.src.SystemServices;
using LiteMonitor.src.SystemServices.InfoService;
using System.Diagnostics;

namespace LiteMonitor.src.WebServer
{
    public class LiteWebServer : IDisposable
    {
        private TcpListener? _listener;
        private volatile bool _isRunning = false;
        private int _currentRunningPort = -1;
        private readonly Settings _cfg;
        
        // [Refactor] Decoupled WebSocket Logic
        private readonly WebSocketSessionManager _wsManager;
        
        public static LiteWebServer? Instance { get; private set; }

        public bool IsRunning => _isRunning;
        public int CurrentRunningPort => _isRunning ? _currentRunningPort : -1;

        public LiteWebServer(Settings cfg)
        {
            _cfg = cfg;
            Instance = this;
            // Initialize WebSocket Manager with the data provider delegate
            _wsManager = new WebSocketSessionManager(WriteSnapshotJson);
        }

        public bool Start(out string errorMsg)
        {
            errorMsg = "";
            if (_isRunning) return true;
            if (!_cfg.WebServerEnabled) return true;

            try
            {
                int port = _cfg.WebServerPort;

                try
                {
                    // [Fix] 优先尝试绑定 IPv6Any 并开启 DualMode，以同时支持 IPv4 和 IPv6
                    _listener = new TcpListener(IPAddress.IPv6Any, port);
                    _listener.Server.DualMode = true;
                    _listener.Start();
                }
                catch (Exception)
                {
                    // 如果系统不支持 IPv6，回退到 IPv4
                    _listener = new TcpListener(IPAddress.Any, port);
                    _listener.Start();
                }

                _isRunning = true;
                _currentRunningPort = port;

                // 1. 启动监听连接的循环
                Task.Run(ListenLoop);
                
                // 2. 启动 WebSocket 管理器 (广播循环在内部)
                _wsManager.Start();
                
                return true;
            }
            catch (Exception ex)
            {
                errorMsg = ex.Message;
                Debug.WriteLine("WebServer Start Error: " + ex.Message);
                return false;
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _currentRunningPort = -1;
            try { _listener?.Stop(); } catch { }
            
            // 停止 WebSocket 管理器
            _wsManager.Stop();
        }

        public void Dispose()
        {
            Stop();
        }

        private async Task ListenLoop()
        {
            while (_isRunning && _listener != null)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleClient(client));
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) { Debug.WriteLine(ex.Message); }
            }
        }

        private void HandleClient(TcpClient client)
        {
            // 注意：这里不要用 using(client)，因为如果是 WebSocket 连接，我们需要保持它存活
            // 只有 HTTP 请求处理完后才关闭
            byte[] buffer = null;
            try
            {
                var stream = client.GetStream();
                // [Optimization] Use ArrayPool for read buffer
                buffer = ArrayPool<byte>.Shared.Rent(4096);
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) { client.Close(); return; }

                string requestStr = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                
                // === 1. 判断是否为 WebSocket 升级请求 ===
                if (Regex.IsMatch(requestStr, "GET", RegexOptions.IgnoreCase) && 
                    Regex.IsMatch(requestStr, "Upgrade: websocket", RegexOptions.IgnoreCase))
                {
                    // [Refactor] 委托给 WebSocketSessionManager 处理
                    if (_wsManager.TryHandleHandshake(client, requestStr))
                    {
                        return; // 握手成功，连接已由 Manager 接管，退出函数
                    }
                }
                
                // === 2. 普通 HTTP 请求处理 ===
                HandleHttpRequest(stream, requestStr);
            }
            catch { }
            finally
            {
                if (buffer != null) ArrayPool<byte>.Shared.Return(buffer);
            }
            
            // 如果不是 WebSocket (即上面的 return 没执行)，则处理完 HTTP 后关闭连接
            // WebSocketManager 内部维护了 active clients 列表，这里不需要再次检查
            // 只要没 return，说明没有升级成功，或者处理完了 HTTP，都应该关闭
            try { client.Close(); } catch { }
        }

        // ★★★ 优化：缓存 Favicon Base64，避免每次重复转换 ★★★
        private static string _cachedFaviconBase64 = null;

        private static string GetAppIconBase64()
        {
            if (_cachedFaviconBase64 != null) return _cachedFaviconBase64;
            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);
                if (icon != null)
                {
                    using var ms = new System.IO.MemoryStream();
                    icon.ToBitmap().Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    _cachedFaviconBase64 = Convert.ToBase64String(ms.ToArray());
                    return _cachedFaviconBase64;
                }
            }
            catch { }
            return ""; // Fallback
        }

        private void HandleHttpRequest(NetworkStream stream, string request)
        {
            string[] lines = request.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return;

            string firstLine = lines[0]; // GET /path HTTP/1.1
            string[] parts = firstLine.Split(' ');
            if (parts.Length < 2) return;
            string path = parts[1];

            string responseBody = "";
            string contentType = "text/html; charset=utf-8";
            int statusCode = 200;

            if (path.StartsWith("/api/snapshot"))
            {
                contentType = "application/json";
                responseBody = GetSnapshotJsonString();
            }
            else if (path == "/" || path == "/index.html" || path.StartsWith("/?"))
            {
                // ★★★ 动态注入 Favicon Base64 ★★★
                string iconBase64 = GetAppIconBase64();
                responseBody = WebPageContent.IndexHtml.Replace("{{FAVICON}}", 
                    !string.IsNullOrEmpty(iconBase64) 
                    ? $"<link rel='icon' type='image/png' href='data:image/png;base64,{iconBase64}'>" 
                    : "");
            }
            else if (path == "/favicon.ico")
            {
                // 如果浏览器请求 favicon.ico，也可以尝试返回二进制流 (可选)
                statusCode = 404; // 暂时保持 404，因为我们已经用了 Base64 内嵌
            }
            else
            {
                string iconBase64 = GetAppIconBase64();
                responseBody = WebPageContent.IndexHtml.Replace("{{FAVICON}}", 
                     !string.IsNullOrEmpty(iconBase64) 
                     ? $"<link rel='icon' type='image/png' href='data:image/png;base64,{iconBase64}'>" 
                     : "");
            }

            SendHttpResponse(stream, statusCode, contentType, responseBody);
        }

        private void SendHttpResponse(NetworkStream stream, int statusCode, string contentType, string body)
        {
            byte[] bodyBuffer = null;
            byte[] headerBuffer = null;
            try
            {
                // 1. Calculate Body Size
                int bodyLen = Encoding.UTF8.GetByteCount(body);
                
                // 2. Build Headers
                var sb = new StringBuilder();
                sb.Append($"HTTP/1.1 {statusCode} OK\r\n");
                sb.Append($"Content-Type: {contentType}\r\n");
                sb.Append($"Content-Length: {bodyLen}\r\n");
                sb.Append("Access-Control-Allow-Origin: *\r\n");
                sb.Append("Connection: close\r\n");
                sb.Append("\r\n");

                string headers = sb.ToString();
                int headerLen = Encoding.UTF8.GetByteCount(headers);

                // 3. Rent & Encode Headers
                headerBuffer = ArrayPool<byte>.Shared.Rent(headerLen);
                Encoding.UTF8.GetBytes(headers, 0, headers.Length, headerBuffer, 0);
                stream.Write(headerBuffer, 0, headerLen);

                // 4. Rent & Encode Body
                if (bodyLen > 0)
                {
                    bodyBuffer = ArrayPool<byte>.Shared.Rent(bodyLen);
                    Encoding.UTF8.GetBytes(body, 0, body.Length, bodyBuffer, 0);
                    stream.Write(bodyBuffer, 0, bodyLen);
                }
            }
            finally
            {
                if (headerBuffer != null) ArrayPool<byte>.Shared.Return(headerBuffer);
                if (bodyBuffer != null) ArrayPool<byte>.Shared.Return(bodyBuffer);
            }
        }

        private string GetSnapshotJsonString()
        {
            using var ms = new System.IO.MemoryStream();
            using var writer = new Utf8JsonWriter(ms);
            WriteSnapshotJson(writer);
            writer.Flush();
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private void WriteSnapshotJson(Utf8JsonWriter writer)
        {
            var hw = HardwareMonitor.Instance;
            if (hw == null)
            {
                writer.WriteStartObject();
                writer.WriteEndObject();
                return;
            }

            // ★★★ 修复：创建列表副本并加锁，防止遍历时 UI 线程修改集合导致崩溃 ★★★
            List<MonitorItemConfig> itemsCopy;
            lock (_cfg.MonitorItems)
            {
                // ★★★ 修复：复用主界面 (Panel模式) 的排序逻辑 ★★★
                itemsCopy = _cfg.MonitorItems
                    .GroupBy(x => x.UIGroup)
                    .OrderBy(g => g.Min(x => x.SortIndex))
                    .SelectMany(g => g.OrderBy(x => x.SortIndex))
                    .ToList();
            }

            string localIp = hw.GetNetworkIP() ?? "127.0.0.1";

            writer.WriteStartObject();

            // "sys" object
            writer.WriteStartObject("sys");
            writer.WriteString("host", InfoService.Instance.GetValue("HOST"));
            writer.WriteString("ip", localIp);
            writer.WriteNumber("port", _currentRunningPort);
            writer.WriteString("uptime", (DateTime.Now - Process.GetCurrentProcess().StartTime).ToString(@"dd\.hh\:mm\:ss"));
            writer.WriteEndObject();

            // "items" array
            writer.WriteStartArray("items");

            foreach (var item in itemsCopy)
            {
                string displayName;
                // [Fix] WebUI should also use MetricLabelResolver to show dynamic names
                string resolved = MetricLabelResolver.ResolveLabel(item);
                if (!string.IsNullOrEmpty(resolved))
                {
                    displayName = resolved;
                }
                else
                {
                    // [Optimization] Use cached keys from MonitorItemConfig
                    string itemsKey = !string.IsNullOrEmpty(item.CachedItemsKey) ? item.CachedItemsKey : UIUtils.Intern("Items." + item.Key);
                    displayName = LanguageManager.T(itemsKey);
                    // 如果翻译失败 (fallback to key suffix)
                    if (displayName.StartsWith("Items.")) displayName = displayName.Split('.')[1];
                }
                
                string groupId = item.UIGroup.ToUpper();
                // [Optimization] Use cached keys
                string groupsKey = !string.IsNullOrEmpty(item.CachedGroupsKey) ? item.CachedGroupsKey : UIUtils.Intern("Groups." + item.UIGroup);
                string groupDisplay = LanguageManager.T(groupsKey);

                string valStr = "";
                string unit = "";
                double pct = 0;
                int status = 0;
                bool isPrimary = false;

                if (item.Key.StartsWith("DASH."))
                {
                    // 特殊处理 DASH.HOST 等纯信息项 (以及插件项)
                    string dashKey = item.Key.Substring(5);
                    valStr = InfoService.Instance.GetValue(dashKey);
                    
                    // ★★★ 修复：读取插件写入的颜色状态 (.Color) ★★★
                    // [Optimization] Use cached Dash keys
                    string colorKey = !string.IsNullOrEmpty(item.CachedDashColorKey) ? item.CachedDashColorKey : dashKey + ".Color";
                    string colorStr = InfoService.Instance.GetValue(colorKey);
                    if (int.TryParse(colorStr, out int cVal)) status = cVal;

                    // 读取插件写入的单位 (.Unit)
                    string unitKey = !string.IsNullOrEmpty(item.CachedDashUnitKey) ? item.CachedDashUnitKey : dashKey + ".Unit";
                    string unitStr = InfoService.Instance.GetValue(unitKey);
                    if (!string.IsNullOrEmpty(unitStr)) unit = unitStr;
                }
                else
                {
                    // 硬件监控项
                    float? val = hw.Get(item.Key);

                    // [Fix] Restore filtering logic: Skip items with no value (e.g. Battery on Desktop)
                    if (!val.HasValue) continue;

                    if (val.HasValue)
                    {
                        // [Refactor] 使用 MetricUtils 统一格式化逻辑，确保与桌面端完全一致 (包括单位换算、颜色阈值、内存显示模式)
                        valStr = MetricUtils.GetValueStr(item.Key, val.Value, false); // false = Panel Context (Standard)
                        unit = MetricUtils.GetUnitStr(item.Key, val.Value, MetricUtils.UnitContext.Panel);
                        
                        // GetProgressValue 返回 0.0-1.0，Web端通常需要 0-100
                        pct = MetricUtils.GetProgressValue(item.Key, val.Value) * 100.0;
                        
                        // 状态判断 (0=Safe, 1=Warn, 2=Crit)
                        status = MetricUtils.GetState(item.Key, val.Value);
                    }
                
                    // Primary Logic
                    if (item.Key == "BAT.Percent") isPrimary = true;
                    else if (item.Key == "CPU.Load") isPrimary = true;
                    else if (item.Key == "MEM.Load") isPrimary = true;
                    else if (item.Key == "GPU.Core.Load" || item.Key == "GPU.Load") isPrimary = true;
                    else isPrimary = false;
                }

                if (string.IsNullOrEmpty(valStr)) valStr = "--";
                
                writer.WriteStartObject();
                writer.WriteString("k", item.Key);
                writer.WriteString("n", displayName);
                writer.WriteString("gid", groupId);
                writer.WriteString("gn", groupDisplay);
                writer.WriteString("v", valStr);
                writer.WriteString("u", unit);
                writer.WriteNumber("pct", pct);
                writer.WriteNumber("sts", status);
                writer.WriteBoolean("primary", isPrimary);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }
}
