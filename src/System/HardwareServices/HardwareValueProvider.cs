using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using LiteMonitor.src.Core;
using Debug = System.Diagnostics.Debug;

namespace LiteMonitor.src.SystemServices
{
    public class HardwareValueProvider : IDisposable
    {
        private readonly Computer _computer;
        private readonly Settings _cfg;
        private readonly SensorMap _sensorMap;
        private readonly NetworkManager _networkManager;
        private readonly DiskManager _diskManager;
        private readonly object _lock;
        private readonly Dictionary<string, float> _lastValidMap; 

        // 系统计数器
        private PerformanceCounter? _cpuPerfCounter;
        private float _lastSystemCpuLoad = 0f;

        // ★★★ [新增 1] Tick 级智能缓存 (防止同帧重复计算) ★★★
        private readonly Dictionary<string, float> _tickCache = new();

        public HardwareValueProvider(Computer c, Settings s, SensorMap map, NetworkManager net, DiskManager disk, object syncLock, Dictionary<string, float> lastValid)
        {
            _computer = c;
            _cfg = s;
            _sensorMap = map;
            _networkManager = net;
            _diskManager = disk;
            _lock = syncLock;
            _lastValidMap = lastValid;
        }

        public void UpdateSystemCpuCounter()
        {
            // ★★★ [新增 2] 每一轮更新开始时，清空本轮缓存 ★★★
            _tickCache.Clear();

            // ... (以下保持原有逻辑) ...
            if (_cfg.UseSystemCpuLoad)
            {
                if (_cpuPerfCounter == null)
                {
                    try { _cpuPerfCounter = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total"); }
                    catch 
                    {
                        try { _cpuPerfCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"); }
                        catch { }
                    }
                    if (_cpuPerfCounter != null) _cpuPerfCounter.NextValue();
                }

                if (_cpuPerfCounter != null)
                {
                    try
                    {
                        float rawVal = _cpuPerfCounter.NextValue();
                        if (rawVal > 100f) rawVal = 100f;
                        _lastSystemCpuLoad = rawVal;
                    }
                    catch { _cpuPerfCounter.Dispose(); _cpuPerfCounter = null; }
                }
            }
            else
            {
                if (_cpuPerfCounter != null) { _cpuPerfCounter.Dispose(); _cpuPerfCounter = null; }
            }
        }

        // ===========================================================
        // ===================== 公共取值入口 =========================
        // ===========================================================
        public float? GetValue(string key)
        {
            // ★★★ [新增 3] 优先查缓存，如果本帧算过，直接返回 ★★★
            if (_tickCache.TryGetValue(key, out float cachedVal)) return cachedVal;

            _sensorMap.EnsureFresh(_computer, _cfg);

            // 定义临时结果变量
            float? result = null;

            // 1. CPU.Load
            if (key == "CPU.Load")
            {
                if (_cfg.UseSystemCpuLoad)
                {
                    result = _lastSystemCpuLoad;
                }
                else
                {
                    // 手动聚合
                    var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
                    if (cpu != null)
                    {
                        double totalLoad = 0;
                        int coreCount = 0;
                        foreach (var s in cpu.Sensors)
                        {
                            if (s.SensorType != SensorType.Load) continue;
                            if (SensorMap.Has(s.Name, "Core") && SensorMap.Has(s.Name, "#") && 
                                !SensorMap.Has(s.Name, "Total") && !SensorMap.Has(s.Name, "SOC") && 
                                !SensorMap.Has(s.Name, "Max") && !SensorMap.Has(s.Name, "Average"))
                            {
                                if (s.Value.HasValue) { totalLoad += s.Value.Value; coreCount++; }
                            }
                        }
                        if (coreCount > 0) result = (float)(totalLoad / coreCount);
                    }
                    
                    // 兜底
                    if (result == null)
                    {
                        lock (_lock) { if (_sensorMap.TryGetSensor("CPU.Load", out var s) && s.Value.HasValue) result = s.Value.Value; }
                    }
                    // 如果还是没值，默认为 0
                    if (result == null) result = 0f;
                }
            }
            // 2. CPU.Temp
            else if (key == "CPU.Temp")
            {
                float maxTemp = -1000f;
                bool found = false;
                var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
                if (cpu != null)
                {
                    foreach (var s in cpu.Sensors)
                    {
                        if (s.SensorType != SensorType.Temperature) continue;
                        if (!s.Value.HasValue || s.Value.Value <= 0) continue;
                        if (SensorMap.Has(s.Name, "Distance") || SensorMap.Has(s.Name, "Average") || SensorMap.Has(s.Name, "Max")) continue;
                        if (s.Value.Value > maxTemp) { maxTemp = s.Value.Value; found = true; }
                    }
                }
                if (found) result = maxTemp;
                
                if (result == null)
                {
                    lock (_lock) { if (_sensorMap.TryGetSensor("CPU.Temp", out var s) && s.Value.HasValue) result = s.Value.Value; }
                }
                if (result == null) result = 0f;
            }
            // 3. 网络与磁盘 (Manager 内部已有一定缓存机制，但这里加一层更稳)
            else if (key.StartsWith("NET"))
            {
                result = _networkManager.GetBestValue(key, _computer, _cfg, _lastValidMap, _lock);
            }
            else if (key.StartsWith("DISK"))
            {
                result = _diskManager.GetBestValue(key, _computer, _cfg, _lastValidMap, _lock);
            }
            // 4. 每日流量
            else if (key == "DATA.DayUp")
            {
                result = TrafficLogger.GetTodayStats().up;
            }
            else if (key == "DATA.DayDown")
            {
                result = TrafficLogger.GetTodayStats().down;
            }
            // 5. 频率与功耗
            else if (key.Contains("Clock") || key.Contains("Power"))
            {
                result = GetCompositeValue(key);
            }
            // 6. 内存
            else if (key == "MEM.Load")
            {
                // 检测总内存逻辑
                if (Settings.DetectedRamTotalGB <= 0)
                {
                    lock (_lock)
                    {
                        if (_sensorMap.TryGetSensor("MEM.Used", out var u) && _sensorMap.TryGetSensor("MEM.Available", out var a))
                        {
                            if (u.Value.HasValue && a.Value.HasValue)
                            {
                                float rawTotal = u.Value.Value + a.Value.Value;
                                Settings.DetectedRamTotalGB = rawTotal > 512.0f ? rawTotal / 1024.0f : rawTotal;
                            }
                        }
                    }
                }
                // 下面会走到通用传感器逻辑去取值
            }
            // 7. 显存
            else if (key == "GPU.VRAM")
            {
                // 注意：这里递归调用了 GetValue，会用到缓存，非常高效
                float? used = GetValue("GPU.VRAM.Used");
                float? total = GetValue("GPU.VRAM.Total");
                if (used.HasValue && total.HasValue && total > 0)
                {
                    if (Settings.DetectedGpuVramTotalGB <= 0) Settings.DetectedGpuVramTotalGB = total.Value / 1024f;
                    // 单位转换
                    if (total > 10485760) { used /= 1048576f; total /= 1048576f; }
                    result = used / total * 100f;
                }
                else
                {
                    lock (_lock) { if (_sensorMap.TryGetSensor("GPU.VRAM.Load", out var s) && s.Value.HasValue) result = s.Value; }
                }
            }
            // 8. 风扇/泵/主板温度 (带 Max 记录)
            else if (key == "CPU.Fan" || key == "CPU.Pump" || key == "CASE.Fan" || key == "GPU.Fan")
            {
                lock (_lock)
                {
                    if (_sensorMap.TryGetSensor(key, out var s) && s.Value.HasValue)
                    {
                        float val = s.Value.Value;
                        _cfg.UpdateMaxRecord(key, val);
                        result = val;
                    }
                }
            }

            // 9. 通用传感器查找 (兜底)
            if (result == null)
            {
                lock (_lock)
                {
                    if (_sensorMap.TryGetSensor(key, out var sensor))
                    {
                        var val = sensor.Value;
                        if (val.HasValue && !float.IsNaN(val.Value)) 
                        { 
                            _lastValidMap[key] = val.Value; 
                            result = val.Value; 
                        }
                        else if (_lastValidMap.TryGetValue(key, out var last))
                        {
                            result = last;
                        }
                    }
                }
            }

            // ★★★ [新增 4] 写入缓存并返回 ★★★
            if (result.HasValue)
            {
                _tickCache[key] = result.Value;
                return result.Value;
            }

            return null;
        }

        // ===========================================================
        // ========= [核心算法] CPU/GPU 频率功耗复合计算 ==============
        // ===========================================================
        // ... (GetCompositeValue 方法保持不变) ...
        private float? GetCompositeValue(string key)
        {
            // 代码无需修改，上面的逻辑已经通过 GetValue 调用到了这里
            // 这里为了节省篇幅省略，请保留你原有的 GetCompositeValue 代码
            if (key == "CPU.Clock")
            {
                if (_sensorMap.CpuCoreCache.Count == 0) return null;
                double sum = 0; int count = 0; float maxRaw = 0;
                float correctionFactor = 1.0f;
                // Zen 5 修正
                if (_sensorMap.CpuBusSpeedSensor != null && _sensorMap.CpuBusSpeedSensor.Value.HasValue)
                {
                    float bus = _sensorMap.CpuBusSpeedSensor.Value.Value;
                    if (bus > 1.0f && bus < 20.0f) { float factor = 100.0f / bus; if (factor > 2.0f && factor < 10.0f) correctionFactor = factor; }
                }
                foreach (var core in _sensorMap.CpuCoreCache)
                {
                    if (core.Clock == null || !core.Clock.Value.HasValue) continue;
                    float clk = core.Clock.Value.Value * correctionFactor;
                    if (clk > maxRaw) maxRaw = clk;
                    // ★★★ 核心逻辑：只过滤明显错误的极低值 ★★★
                    if (clk > 400f) { sum += clk; count++; }
                }
                if (maxRaw > 0) _cfg.UpdateMaxRecord(key, maxRaw);
                if (count > 0) return (float)(sum / count);
                return maxRaw;
            }
            if (key == "CPU.Power")
            {
                lock (_lock) { if (_sensorMap.TryGetSensor("CPU.Power", out var s) && s.Value.HasValue) { _cfg.UpdateMaxRecord(key, s.Value.Value); return s.Value.Value; } }
                return null;
            }
            if (key.StartsWith("GPU"))
            {
                var gpu = _sensorMap.CachedGpu;
                if (gpu == null) return null;
                if (key == "GPU.Clock")
                {
                    var s = gpu.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Clock && (SensorMap.Has(x.Name, "graphics") || SensorMap.Has(x.Name, "core") || SensorMap.Has(x.Name, "shader")));
                    // ★★★ 【修复 1】频率异常过滤 ★★★
                    if (s != null && s.Value.HasValue) { float val = s.Value.Value; if (val > 6000.0f) return null; _cfg.UpdateMaxRecord(key, val); return val; }
                }
                else if (key == "GPU.Power")
                {
                    var s = gpu.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Power && (SensorMap.Has(x.Name, "package") || SensorMap.Has(x.Name, "ppt") || SensorMap.Has(x.Name, "board") || SensorMap.Has(x.Name, "core") || SensorMap.Has(x.Name, "gpu")));
                    // ★★★ 【修复 2】功耗异常过滤 ★★★
                    if (s != null && s.Value.HasValue) { float val = s.Value.Value; if (val > 2000.0f) return null; _cfg.UpdateMaxRecord(key, val); return val; }
                }
            }
            return null;
        }

        public void Dispose()
        {
            _cpuPerfCounter?.Dispose();
        }
    }
}