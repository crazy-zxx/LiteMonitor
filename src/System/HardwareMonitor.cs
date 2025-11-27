using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using LibreHardwareMonitor.Hardware;
using System.Threading.Tasks; // 确保引入 Task

namespace LiteMonitor.src.System
{
    public sealed class HardwareMonitor : IDisposable
    {
        // =======================================================================
        // [新增] 线程安全锁与智能缓存字段
        // =======================================================================
        private readonly object _lock = new object(); // 核心锁：保护 _map 和 _lastValid

        // 网络硬件缓存 (CPU 优化)
        private IHardware? _cachedNetHw;
        private DateTime _lastNetScan = DateTime.MinValue;

        // 磁盘硬件缓存 (CPU 优化)
        private IHardware? _cachedDiskHw;
        private DateTime _lastDiskScan = DateTime.MinValue;

        // =======================================================================

        private readonly Computer _computer;
        private readonly Dictionary<string, ISensor> _map = new();
        private readonly Dictionary<string, float> _lastValid = new();
        private DateTime _lastMapBuild = DateTime.MinValue;

        private readonly Settings _cfg;

        public static HardwareMonitor? Instance { get; private set; }

        public event Action? OnValuesUpdated;

        public HardwareMonitor(Settings cfg)
        {
            _cfg = cfg;
            Instance = this;

            _computer = new Computer()
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsNetworkEnabled = true,
                IsStorageEnabled = true,
                IsMotherboardEnabled = false,
                IsControllerEnabled = false
            };

            Task.Run(() =>
            {
                try
                {
                    _computer.Open();
                    BuildSensorMap();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[HardwareMonitor] init failed: " + ex.Message);
                }
            });
        }

        // ===========================================================
        // ========== Sensor Map 建立（CPU/GPU/MEM） ================
        // ===========================================================
        // [修改说明] 重构为线程安全版本：先局部构建，再加锁交换，防止 UI 读取时崩溃
        private void BuildSensorMap()
        {
            // 1. 创建临时字典 (局部变量，线程安全)
            var newMap = new Dictionary<string, ISensor>();

            // 定义局部递归函数，替代原有的 RegisterHardware
            void RegisterTo(IHardware hw)
            {
                hw.Update();

                foreach (var s in hw.Sensors)
                {
                    string? key = NormalizeKey(hw, s);
                    // 使用临时字典 newMap
                    if (!string.IsNullOrEmpty(key) && !newMap.ContainsKey(key))
                        newMap[key] = s;
                }

                foreach (var sub in hw.SubHardware)
                    RegisterTo(sub);
            }

            // ⭐ 按优先级排序：独显(GpuNvidia > GpuAmd) > 核显(GpuIntel) > 其他
            var ordered = _computer.Hardware.OrderBy(h => GetHwPriority(h));

            foreach (var hw in ordered)
                RegisterTo(hw);

            // 2. 仅在数据交换瞬间加锁
            lock (_lock)
            {
                _map.Clear();
                foreach (var kv in newMap) _map[kv.Key] = kv.Value;
                _lastMapBuild = DateTime.Now;
            }
        }

        private static int GetHwPriority(IHardware hw)
        {
            return hw.HardwareType switch
            {
                HardwareType.GpuNvidia => 0, // 独显最高优先级
                HardwareType.GpuAmd => 1,
                HardwareType.GpuIntel => 2, // 核显靠后
                _ => 3  // 其他最后
            };
        }

        // [注意] 原 RegisterHardware 方法已整合进 BuildSensorMap 的局部函数 RegisterTo 中，故移除

        // [修改说明] 移除 ToLower()，使用 Has() 进行零内存分配匹配，保留原始逻辑
        private static string? NormalizeKey(IHardware hw, ISensor s)
        {
            string name = s.Name; // [优化] 移除 ToLower()，减少 GC
            var type = hw.HardwareType;

            // ========== CPU ==========
            if (type == HardwareType.Cpu)
            {
                if (s.SensorType == SensorType.Load && Has(name, "total"))
                    return "CPU.Load";

                if (s.SensorType == SensorType.Temperature)
                {
                    if (Has(name, "average") || Has(name, "core average"))
                        return "CPU.Temp";
                    if (Has(name, "package") || Has(name, "tctl"))
                        return "CPU.Temp";
                    if (Has(name, "cores"))
                        return "CPU.Temp";
                }
            }

            // ========== GPU ==========
            if (type is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
            {
                if (s.SensorType == SensorType.Load &&
                  (Has(name, "core") || Has(name, "d3d 3d")))
                    return "GPU.Load";

                if (s.SensorType == SensorType.Temperature &&
                    (Has(name, "core") || Has(name, "hot spot") // [保留] 原有热点逻辑
                    || Has(name, "gpu vr soc") // 集成显卡核心温度
                    ))
                    return "GPU.Temp";

                if (s.SensorType == SensorType.SmallData)
                {
                    if ((Has(name, "memory") || Has(name, "dedicated")) && Has(name, "used"))
                        return "GPU.VRAM.Used";
                    if ((Has(name, "memory") || Has(name, "dedicated")) && Has(name, "total"))
                        return "GPU.VRAM.Total";
                }

                if (s.SensorType == SensorType.Load && Has(name, "memory"))
                    return "GPU.VRAM.Load";
            }

            // ========== Memory ==========
            if (type == HardwareType.Memory)
            {
                if (s.SensorType == SensorType.Load && Has(name, "memory"))
                    return "MEM.Load";
            }

            return null;
        }

        private void EnsureMapFresh()
        {
            if ((DateTime.Now - _lastMapBuild).TotalMinutes > 10)
                BuildSensorMap();
        }

        // ===========================================================
        // ===================== 核心 Get ============================
        // ===========================================================
        // [修改说明] 增加 lock (_lock) 保护字典读取
        public float? Get(string key)
        {
            EnsureMapFresh();

            switch (key)
            {
                case "NET.Up":
                case "NET.Down":
                    return GetNetworkValue(key);

                case "DISK.Read":
                case "DISK.Write":
                    return GetDiskValue(key);
            }

            // ===== GPU VRAM 额外计算 =====
            if (key == "GPU.VRAM")
            {
                // 递归调用 Get，内部会自动加锁，所以这里不需要显式锁
                float? used = Get("GPU.VRAM.Used");
                float? total = Get("GPU.VRAM.Total");
                if (used.HasValue && total.HasValue && total > 0)
                {
                    if (total > 1024 * 1024 * 10)
                    {
                        used /= 1024f * 1024f;
                        total /= 1024f * 1024f;
                    }
                    return used / total * 100f;
                }
                
                // 直接读取 _map 需要加锁
                lock (_lock)
                {
                    if (_map.TryGetValue("GPU.VRAM.Load", out var s) && s.Value.HasValue)
                        return s.Value;
                }
                return null;
            }

            // ===== 普通传感器 (加锁保护) =====
            lock (_lock)
            {
                if (_map.TryGetValue(key, out var sensor))
                {
                    var val = sensor.Value;
                    if (val.HasValue && !float.IsNaN(val.Value))
                    {
                        _lastValid[key] = val.Value;
                        return val.Value;
                    }
                    if (_lastValid.TryGetValue(key, out var last))
                        return last;
                }
            }

            return null;
        }

        // ===========================================================
        // =============== 手动 / 自动 — 网卡 ========================
        // ===========================================================
        private float? GetNetworkValue(string key)
        {
            // ========== 手动模式 ==========
            if (!string.IsNullOrWhiteSpace(_cfg.PreferredNetwork))
            {
                var hw = _computer.Hardware
                    .FirstOrDefault(h =>
                        h.HardwareType == HardwareType.Network &&
                        h.Name.Equals(_cfg.PreferredNetwork, StringComparison.OrdinalIgnoreCase));

                if (hw != null)
                    return ReadNetworkSensor(hw, key);

                // 找不到 → 回到自动
            }

            return GetBestNetworkValue(key);
        }

        // --- 帮助函数：从指定网卡读取 Up/Down ---
        // [修改说明] 使用 Has 优化，且在读写 _lastValid 时加锁
        private float? ReadNetworkSensor(IHardware hw, string key)
        {
            ISensor? up = null;
            ISensor? down = null;

            foreach (var s in hw.Sensors)
            {
                if (s.SensorType != SensorType.Throughput) continue;

                string sn = s.Name; // 原为 ToLower()，现优化

                if (_upKW.Any(k => Has(sn, k))) up ??= s;
                if (_downKW.Any(k => Has(sn, k))) down ??= s;
            }

            ISensor? t = key == "NET.Up" ? up : down;

            if (t?.Value is float v && !float.IsNaN(v))
            {
                lock (_lock) _lastValid[key] = v; // 加锁写入
                return v;
            }

            lock (_lock) // 加锁读取
            {
                if (_lastValid.TryGetValue(key, out var last))
                    return last;
            }

            return null;
        }

        private static readonly string[] _upKW = { "upload", "up", "sent", "send", "tx", "transmit" };
        private static readonly string[] _downKW = { "download", "down", "received", "receive", "rx" };
        private static readonly string[] _virtualNicKW =
        {
            "virtual","vmware","hyper-v","hyper v","vbox",
            "loopback","tunnel","tap","tun","bluetooth",
            "zerotier","tailscale","wi-fi direct","wifi direct","wan miniport"
        };

        // --- 自动：选择最活跃网卡 ---
        // [修改说明] 引入智能缓存策略，使用 Has 优化，加锁写入
               private float? GetBestNetworkValue(string key)
        {
            // 1. 尝试使用缓存
            if (_cachedNetHw != null)
            {
                float? cachedVal = ReadNetworkSensor(_cachedNetHw, key); // ★ 改名 v -> cachedVal
                
                // 如果有流量(>0)，说明它活跃，无需扫描，续期缓存
                if (cachedVal.HasValue && cachedVal.Value > 0.1f)
                {
                    _lastNetScan = DateTime.Now;
                    return cachedVal;
                }
                // 如果无流量，但距离上次扫描不足10秒，也不扫描 (冷却期)
                if ((DateTime.Now - _lastNetScan).TotalSeconds < 10)
                    return cachedVal;
            }

            // 2. 全盘扫描 (代码保持不变)
            ISensor? bestUp = null;
            ISensor? bestDown = null;
            double bestScore = double.MinValue;
            IHardware? bestHw = null;

            foreach (var hw in _computer.Hardware.Where(h => h.HardwareType == HardwareType.Network))
            {
                string lname = hw.Name;
                double penalty = _virtualNicKW.Any(val => Has(lname, val)) ? -1e9 : 0; // ★ lambda变量改名 v -> val

                ISensor? up = null;
                ISensor? down = null;

                foreach (var s in hw.Sensors)
                {
                    if (s.SensorType != SensorType.Throughput) continue;

                    string sn = s.Name;
                    if (_upKW.Any(k => Has(sn, k))) up ??= s;
                    if (_downKW.Any(k => Has(sn, k))) down ??= s;
                }

                if (up == null && down == null) continue;

                double score = (up?.Value ?? 0) + (down?.Value ?? 0) + penalty;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestUp = up;
                    bestDown = down;
                    bestHw = hw;
                }
            }

            // 3. 更新缓存
            if (bestHw != null)
            {
                _cachedNetHw = bestHw;
                _lastNetScan = DateTime.Now;
            }

            ISensor? t = key == "NET.Up" ? bestUp : bestDown;

            // 4. 线程安全返回 (这里的 v 可以保留，因为 cachedVal 作用域在 if 里面，不冲突了，但为了保险起见)
            if (t?.Value is float finalVal && !float.IsNaN(finalVal)) // ★ 改名 v -> finalVal
            {
                lock (_lock) _lastValid[key] = finalVal;
                return finalVal;
            }

            lock (_lock)
            {
                if (_lastValid.TryGetValue(key, out var last)) return last;
            }
            return null;
        }


        // ===========================================================
        // =============== 手动 / 自动 — 磁盘 =========================
        // ===========================================================
        private float? GetDiskValue(string key)
        {
            // ========== 手动模式 ==========
            if (!string.IsNullOrWhiteSpace(_cfg.PreferredDisk))
            {
                var hw = _computer.Hardware
                    .FirstOrDefault(h =>
                        h.HardwareType == HardwareType.Storage &&
                        h.Name.Equals(_cfg.PreferredDisk, StringComparison.OrdinalIgnoreCase));

                if (hw != null)
                    return ReadDiskSensor(hw, key);
            }

            return GetBestDiskValue(key);
        }

        // --- 帮助：从指定磁盘读取 ---
        // [修改说明] 使用 Has 优化，加锁保护
        private float? ReadDiskSensor(IHardware hw, string key)
        {
            ISensor? read = null;
            ISensor? write = null;

            foreach (var s in hw.Sensors)
            {
                if (s.SensorType != SensorType.Throughput) continue;

                string sn = s.Name; // 原为 ToLower
                if (Has(sn, "read")) read ??= s;
                if (Has(sn, "write")) write ??= s;
            }

            ISensor? t = key == "DISK.Read" ? read : write;

            if (t?.Value is float v && !float.IsNaN(v))
            {
                lock (_lock) _lastValid[key] = v; // 加锁
                return v;
            }

            lock (_lock) // 加锁
            {
                if (_lastValid.TryGetValue(key, out var last))
                    return last;
            }

            return null;
        }

        // --- 自动：系统盘优先 + 活跃度 ---
        // [修改说明] 引入智能缓存策略，使用 Has 优化，加锁写入
               private float? GetBestDiskValue(string key)
        {
            // 1. 尝试使用缓存
            if (_cachedDiskHw != null)
            {
                float? cachedVal = ReadDiskSensor(_cachedDiskHw, key); // ★ 改名 v -> cachedVal
                if (cachedVal.HasValue && cachedVal.Value > 0.1f)
                {
                    _lastDiskScan = DateTime.Now;
                    return cachedVal;
                }
                if ((DateTime.Now - _lastDiskScan).TotalSeconds < 10)// 冷却期10秒
                    return cachedVal;
            }

            // 2. 全盘扫描
            string sysStr = "";
            try {
                string path = Environment.SystemDirectory;
                string root = Path.GetPathRoot(path);
                if (!string.IsNullOrEmpty(root)) sysStr = root.Substring(0, 2);
            } catch { }

            ISensor? bestRead = null;
            ISensor? bestWrite = null;
            double bestScore = double.MinValue;
            IHardware? bestHw = null;

            foreach (var hw in _computer.Hardware.Where(h => h.HardwareType == HardwareType.Storage))
            {
                bool isSystemDisk = false;
                string lname = hw.Name;

                if (sysStr != "")
                {
                    if (Has(lname, sysStr)) isSystemDisk = true;
                    else {
                        foreach(var s in hw.Sensors) 
                            if(Has(s.Name, sysStr)) { isSystemDisk = true; break; }
                    }
                }

                ISensor? read = null;
                ISensor? write = null;
                foreach (var s in hw.Sensors)
                {
                    if (s.SensorType != SensorType.Throughput) continue;
                    string sn = s.Name;
                    if (Has(sn, "read")) read ??= s;
                    if (Has(sn, "write")) write ??= s;
                }

                if (read == null && write == null) continue;
                double score = (read?.Value ?? 0) + (write?.Value ?? 0);
                if (isSystemDisk) score += 1e9;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestRead = read;
                    bestWrite = write;
                    bestHw = hw;
                }
            }

            // 3. 更新缓存
            if (bestHw != null)
            {
                _cachedDiskHw = bestHw;
                _lastDiskScan = DateTime.Now;
            }

            ISensor? t = key == "DISK.Read" ? bestRead : bestWrite;

            // 4. 线程安全返回
            if (t?.Value is float finalVal && !float.IsNaN(finalVal)) // ★ 改名 v -> finalVal
            {
                lock (_lock) _lastValid[key] = finalVal;
                return finalVal;
            }

            lock (_lock) { if (_lastValid.TryGetValue(key, out var last)) return last; }
            return null;
        }

        // ===========================================================
        // =============== 用于菜单枚举设备 ==========================
        // ===========================================================
        public static List<string> ListAllNetworks()
        {
            if (Instance == null) return new List<string>();

            return Instance._computer.Hardware
                .Where(h => h.HardwareType == HardwareType.Network)
                .Select(h => h.Name)
                .Distinct()
                .ToList();
        }

        public static List<string> ListAllDisks()
        {
            if (Instance == null) return new List<string>();

            return Instance._computer.Hardware
                .Where(h => h.HardwareType == HardwareType.Storage)
                .Select(h => h.Name)
                .Distinct()
                .ToList();
        }

        // ===========================================================
        public void UpdateAll()
        {
            try
            {
                foreach (var hw in _computer.Hardware)
                {
                    if (hw.HardwareType is HardwareType.GpuNvidia
                        or HardwareType.GpuAmd
                        or HardwareType.GpuIntel
                        or HardwareType.Cpu)
                        hw.Update();
                    else if ((DateTime.Now - _lastMapBuild).TotalSeconds > 3)
                        hw.Update();
                }

                OnValuesUpdated?.Invoke();
            }
            catch { }
        }

        public void Dispose() => _computer.Close();

        // [新增] 高性能字符串包含检查（辅助方法）
        private static bool Has(string source, string sub)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(sub)) return false;
            return source.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
