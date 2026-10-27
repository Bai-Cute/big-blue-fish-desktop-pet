using System.Diagnostics;
using System.IO;
using BigBlueFish.Windows;

internal static class InferenceDeviceVerification
{
    internal static void Rules()
    {
        int count = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); count++; }
        var devices = new CompanionInferenceDevices();
        Check(devices.Describe(false) == "正在确认运行设备", "not loaded is not a claimed GPU");
        devices.Observe("Vulkan0: AMD Radeon(TM) Graphics (8886 MiB, 8441 MiB free)");
        devices.Observe("Vulkan1: NVIDIA GeForce RTX 3050 Laptop GPU (3979 MiB, 3382 MiB free)");
        Check(devices.Describe(true) == "运行设备未确认", "enumeration alone does not prove actual placement");
        devices.Observe("0.04.498.307 I llama_prepare_model_devices: using device Vulkan1 (NVIDIA GeForce RTX 3050 Laptop GPU) (0000:01:00.0) - 3379 MiB free");
        devices.Observe("load_tensors: Vulkan_Host model buffer size = 497.31 MiB");
        devices.Observe("load_tensors: Vulkan0 model buffer size = 0.00 MiB");
        Check(!devices.Describe(true).StartsWith("GPU"), "zero allocations and host RAM are not GPUs in use");
        devices.Observe("load_tensors: CPU_Mapped model buffer size = 566.32 MiB");
        Check(devices.Describe(true) == "CPU", "CPU-only weights are identified even if GPU was enumerated");
        devices.Observe("0.05.016.949 I load_tensors: Vulkan1 model buffer size = 2502.62 MiB");
        Check(devices.Describe(true) == "GPU：NVIDIA GeForce RTX 3050 Laptop GPU（Vulkan1）", "actual discrete allocation is named without integrated device");
        devices.Observe("llama_prepare_model_devices: using device Vulkan2 (Intel(R) Arc(TM) B580 Graphics) (unknown id) - 11000 MiB free");
        devices.Observe("load_tensors: Vulkan2 model buffer size = 1024.25 MiB");
        Check(devices.Describe(true).Contains("Vulkan1") && devices.Describe(true).Contains("Vulkan2"), "multiple GPUs can be reported instead of a fabricated single choice");
        devices.Observe("load_tensors: Vulkan9 model buffer size = NaN MiB");
        Check(!devices.Describe(true).Contains("Vulkan9"), "malformed allocation is ignored");
        var integrated = new CompanionInferenceDevices();
        integrated.Observe("llama_prepare_model_devices: using device Vulkan0 (Intel(R) Arc(TM) 140V GPU (16GB)) (unknown id) - 17641 MiB free");
        integrated.Observe("load_tensors: Vulkan0 model buffer size = 2572.86 MiB");
        Check(integrated.Describe(true).Contains("Intel(R) Arc(TM) 140V GPU (16GB)"), "integrated GPU and nested parentheses are preserved");
        Console.WriteLine($"RESULT {count} inference device reporting checks passed");
    }

    internal static async Task<int> Live(string installation)
    {
        string directory = Path.GetFullPath(installation);
        Environment.CurrentDirectory = Path.Combine(directory, "data");
        Process? Start(ProcessStartInfo info)
        {
            var process = Process.Start(info);
            if (process != null)
            {
                void Show(string? line)
                {
                    if (line != null && (line.Contains("using device") || line.Contains("model buffer") || line.Contains("verbosity")))
                        Console.WriteLine("STARTUP " + line);
                }
                process.OutputDataReceived += (_, e) => Show(e.Data);
                process.ErrorDataReceived += (_, e) => Show(e.Data);
            }
            return process;
        }
        using var brain = new CompanionBrain(Start, TimeSpan.FromSeconds(60),
            runtimeDirectory: Path.Combine(directory, "runtime")) { StopOnBattery = false };
        await brain.WarmUp();
        if (brain.RunnerPid == null) throw new Exception(brain.Status);
        Console.WriteLine("REAL MODEL DEVICE " + brain.ExecutionDevice);
        if (!brain.ExecutionDevice.StartsWith("GPU：") || brain.ExecutionDevice.Contains("未确认"))
            throw new Exception("Actual local GPU model placement was not observed");
        brain.Stop();
        if (brain.ExecutionDevice != "未运行") throw new Exception("Stopped runner still claims a device");
        return 0;
    }
}
