using System.Runtime.InteropServices;

namespace Dettatura.Core.Transcription;

/// <summary>
/// Elenca le GPU come le vede Vulkan, nello stesso ordine usato da whisper.cpp.
/// Sui portatili con grafica ibrida la GPU 0 è spesso l'integrata, venti volte più lenta della dedicata.
/// </summary>
public static class VulkanDevices
{
    private const int DeviceTypeDiscrete = 2;
    private const int PropertiesSize = 1024; // VkPhysicalDeviceProperties occupa ~820 byte
    private const int DeviceTypeOffset = 16;
    private const int DeviceNameOffset = 20;

    /// <summary>Indice e nome della prima GPU dedicata; null se non c'è o Vulkan non è installato.</summary>
    public static (int Index, string Name)? FindDiscreteGpu() =>
        List().Where(d => d.Discrete).Select(d => ((int, string)?)(d.Index, d.Name)).FirstOrDefault();

    /// <summary>Tutte le GPU Vulkan nell'ordine di whisper.cpp; vuoto se Vulkan non è installato.</summary>
    public static IReadOnlyList<(int Index, string Name, bool Discrete)> List()
    {
        var result = new List<(int, string, bool)>();
        try
        {
            var info = new InstanceCreateInfo { SType = 1 };
            if (vkCreateInstance(ref info, IntPtr.Zero, out var instance) != 0) return result;
            var properties = Marshal.AllocHGlobal(PropertiesSize);
            try
            {
                uint count = 0;
                vkEnumeratePhysicalDevices(instance, ref count, null);
                var devices = new IntPtr[count];
                vkEnumeratePhysicalDevices(instance, ref count, devices);
                for (var i = 0; i < count; i++)
                {
                    vkGetPhysicalDeviceProperties(devices[i], properties);
                    result.Add((i, Marshal.PtrToStringAnsi(properties + DeviceNameOffset) ?? "GPU",
                        Marshal.ReadInt32(properties, DeviceTypeOffset) == DeviceTypeDiscrete));
                }
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(properties);
                vkDestroyInstance(instance, IntPtr.Zero);
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return result;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InstanceCreateInfo
    {
        public int SType;
        public IntPtr Next;
        public uint Flags;
        public IntPtr ApplicationInfo;
        public uint EnabledLayerCount;
        public IntPtr EnabledLayerNames;
        public uint EnabledExtensionCount;
        public IntPtr EnabledExtensionNames;
    }

    [DllImport("vulkan-1")]
    private static extern int vkCreateInstance(ref InstanceCreateInfo createInfo, IntPtr allocator, out IntPtr instance);

    [DllImport("vulkan-1")]
    private static extern void vkDestroyInstance(IntPtr instance, IntPtr allocator);

    [DllImport("vulkan-1")]
    private static extern int vkEnumeratePhysicalDevices(IntPtr instance, ref uint count, IntPtr[]? devices);

    [DllImport("vulkan-1")]
    private static extern void vkGetPhysicalDeviceProperties(IntPtr device, IntPtr properties);
}
