using System;
using System.Runtime.InteropServices;

namespace NoesisGodot;

/// <summary>
/// Minimal Vulkan P/Invoke surface for creating an RGBA8 image whose memory is exported as an opaque Win32 handle
/// (VK_KHR_external_memory_win32), so a GL context can import the SAME GPU memory via GL_EXT_memory_object_win32.
///
/// All handles come from Godot's own Vulkan device (RenderingDevice.GetDriverResource) which we allocate on Godot's device
/// so Godot can sample the image directly (TextureCreateFromExtension).
/// </summary>
internal static class VulkanInterop
{
    public readonly struct ExportedImage
    {
        public readonly ulong Image; // VkImage
        public readonly ulong Memory; // VkDeviceMemory
        public readonly IntPtr Win32Handle; // opaque handle for GL import
        public readonly ulong AllocationSize;

        public ExportedImage(ulong image, ulong memory, IntPtr handle, ulong size)
        {
            Image = image;
            Memory = memory;
            Win32Handle = handle;
            AllocationSize = size;
        }
    }

    /// <summary>True if the device can export memory as Win32 handles (VK_KHR_external_memory_win32 enabled at device creation).</summary>
    public static bool SupportsWin32HandleExport(IntPtr device) =>
        device != IntPtr.Zero &&
        vkGetDeviceProcAddr(device, "vkGetMemoryWin32HandleKHR") != IntPtr.Zero;

    /// <summary>Creates a 2D RGBA8 optimal-tiling image with exportable dedicated memory on the given device.
    /// Throws with a diagnostic on any failure.</summary>
    public static ExportedImage CreateExportedImage(IntPtr device, IntPtr physicalDevice, uint width, uint height)
    {
        // --- image (with external-memory chain) --------------------------
        var externalInfo = new VkExternalMemoryImageCreateInfo
        {
            sType = VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO,
            handleTypes = VK_EXTERNAL_MEMORY_HANDLE_TYPE_OPAQUE_WIN32_BIT,
        };
        IntPtr externalInfoPtr = StructToPtr(externalInfo);

        var imageInfo = new VkImageCreateInfo
        {
            sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO,
            pNext = externalInfoPtr,
            imageType = VK_IMAGE_TYPE_2D,
            format = VK_FORMAT_R8G8B8A8_UNORM,
            extentWidth = width,
            extentHeight = height,
            extentDepth = 1,
            mipLevels = 1,
            arrayLayers = 1,
            samples = VK_SAMPLE_COUNT_1_BIT,
            tiling = VK_IMAGE_TILING_OPTIMAL,
            usage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT | VK_IMAGE_USAGE_SAMPLED_BIT |
                    VK_IMAGE_USAGE_TRANSFER_SRC_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT,
            sharingMode = VK_SHARING_MODE_EXCLUSIVE,
            initialLayout = VK_IMAGE_LAYOUT_UNDEFINED,
        };

        ulong image;
        int result;
        try
        {
            result = vkCreateImage(device, ref imageInfo, IntPtr.Zero, out image);
        }
        finally
        {
            Marshal.FreeHGlobal(externalInfoPtr);
        }
        Check(result, "vkCreateImage");

        try
        {
            // --- memory (exportable + dedicated) -------------------------
            vkGetImageMemoryRequirements(device, image, out VkMemoryRequirements requirements);
            uint memoryTypeIndex = FindDeviceLocalMemoryType(physicalDevice, requirements.memoryTypeBits);

            var dedicatedInfo = new VkMemoryDedicatedAllocateInfo
            {
                sType = VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO,
                image = image,
            };
            IntPtr dedicatedPtr = StructToPtr(dedicatedInfo);

            var exportInfo = new VkExportMemoryAllocateInfo
            {
                sType = VK_STRUCTURE_TYPE_EXPORT_MEMORY_ALLOCATE_INFO,
                pNext = dedicatedPtr,
                handleTypes = VK_EXTERNAL_MEMORY_HANDLE_TYPE_OPAQUE_WIN32_BIT,
            };
            IntPtr exportPtr = StructToPtr(exportInfo);

            var allocateInfo = new VkMemoryAllocateInfo
            {
                sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,
                pNext = exportPtr,
                allocationSize = requirements.size,
                memoryTypeIndex = memoryTypeIndex,
            };

            ulong memory;
            try
            {
                Check(vkAllocateMemory(device, ref allocateInfo, IntPtr.Zero, out memory), "vkAllocateMemory");
            }
            finally
            {
                Marshal.FreeHGlobal(exportPtr);
                Marshal.FreeHGlobal(dedicatedPtr);
            }

            try
            {
                Check(vkBindImageMemory(device, image, memory, 0), "vkBindImageMemory");

                // Export the handle
                IntPtr getHandleFn = vkGetDeviceProcAddr(device, "vkGetMemoryWin32HandleKHR");
                if (getHandleFn == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        "vkGetMemoryWin32HandleKHR unavailable — Godot's Vulkan device was created " +
                        "without VK_KHR_external_memory_win32.");
                }
                var getHandle = Marshal.GetDelegateForFunctionPointer<VkGetMemoryWin32HandleKHRProc>(getHandleFn);

                var handleInfo = new VkMemoryGetWin32HandleInfoKHR
                {
                    sType = VK_STRUCTURE_TYPE_MEMORY_GET_WIN32_HANDLE_INFO_KHR,
                    memory = memory,
                    handleType = VK_EXTERNAL_MEMORY_HANDLE_TYPE_OPAQUE_WIN32_BIT,
                };
                Check(getHandle(device, ref handleInfo, out IntPtr win32Handle), "vkGetMemoryWin32HandleKHR");

                return new ExportedImage(image, memory, win32Handle, requirements.size);
            }
            catch
            {
                vkFreeMemory(device, memory, IntPtr.Zero);
                throw;
            }
        }
        catch
        {
            vkDestroyImage(device, image, IntPtr.Zero);
            throw;
        }
    }

    public static void DestroyExportedImage(IntPtr device, in ExportedImage exported)
    {
        if (exported.Win32Handle != IntPtr.Zero)
        {
            CloseHandle(exported.Win32Handle);
        }
        if (exported.Image != 0)
        {
            vkDestroyImage(device, exported.Image, IntPtr.Zero);
        }
        if (exported.Memory != 0)
        {
            vkFreeMemory(device, exported.Memory, IntPtr.Zero);
        }
    }

    private static uint FindDeviceLocalMemoryType(IntPtr physicalDevice, uint typeBits)
    {
        vkGetPhysicalDeviceMemoryProperties(physicalDevice, out VkPhysicalDeviceMemoryProperties props);
        for (uint i = 0; i < props.memoryTypeCount; i++)
        {
            bool allowed = (typeBits & (1u << (int)i)) != 0;
            bool deviceLocal = (props.memoryTypes[i].propertyFlags & VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT) != 0;
            if (allowed && deviceLocal)
            {
                return i;
            }
        }
        throw new InvalidOperationException("No device-local memory type for exported image.");
    }

    private static IntPtr StructToPtr<T>(T value) where T : struct
    {
        IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
        Marshal.StructureToPtr(value, ptr, false);
        return ptr;
    }

    private static void Check(int result, string call)
    {
        if (result != 0)
        {
            throw new InvalidOperationException($"{call} failed (VkResult {result}).");
        }
    }

    private const int VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO = 14;
    private const int VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO = 5;
    private const int VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO = 1000072001;
    private const int VK_STRUCTURE_TYPE_EXPORT_MEMORY_ALLOCATE_INFO = 1000072002;
    private const int VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO = 1000127001;
    private const int VK_STRUCTURE_TYPE_MEMORY_GET_WIN32_HANDLE_INFO_KHR = 1000073003;

    private const uint VK_EXTERNAL_MEMORY_HANDLE_TYPE_OPAQUE_WIN32_BIT = 0x1;
    private const int VK_IMAGE_TYPE_2D = 1;
    private const int VK_FORMAT_R8G8B8A8_UNORM = 37;
    private const int VK_SAMPLE_COUNT_1_BIT = 1;
    private const int VK_IMAGE_TILING_OPTIMAL = 0;
    private const uint VK_IMAGE_USAGE_TRANSFER_SRC_BIT = 0x1;
    private const uint VK_IMAGE_USAGE_TRANSFER_DST_BIT = 0x2;
    private const uint VK_IMAGE_USAGE_SAMPLED_BIT = 0x4;
    private const uint VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT = 0x10;
    private const int VK_SHARING_MODE_EXCLUSIVE = 0;
    private const int VK_IMAGE_LAYOUT_UNDEFINED = 0;
    private const uint VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct VkImageCreateInfo
    {
        public int sType;
        public IntPtr pNext;
        public uint flags;
        public int imageType;
        public int format;
        public uint extentWidth, extentHeight, extentDepth;
        public uint mipLevels;
        public uint arrayLayers;
        public int samples;
        public int tiling;
        public uint usage;
        public int sharingMode;
        public uint queueFamilyIndexCount;
        public IntPtr pQueueFamilyIndices;
        public int initialLayout;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VkExternalMemoryImageCreateInfo
    {
        public int sType;
        public IntPtr pNext;
        public uint handleTypes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VkMemoryRequirements
    {
        public ulong size;
        public ulong alignment;
        public uint memoryTypeBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VkMemoryAllocateInfo
    {
        public int sType;
        public IntPtr pNext;
        public ulong allocationSize;
        public uint memoryTypeIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VkExportMemoryAllocateInfo
    {
        public int sType;
        public IntPtr pNext;
        public uint handleTypes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VkMemoryDedicatedAllocateInfo
    {
        public int sType;
        public IntPtr pNext;
        public ulong image;
        public ulong buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VkMemoryGetWin32HandleInfoKHR
    {
        public int sType;
        public IntPtr pNext;
        public ulong memory;
        public uint handleType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VkMemoryType
    {
        public uint propertyFlags;
        public uint heapIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VkMemoryHeap
    {
        public ulong size;
        public uint flags;
        private uint _pad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VkPhysicalDeviceMemoryProperties
    {
        public uint memoryTypeCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public VkMemoryType[] memoryTypes;
        public uint memoryHeapCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public VkMemoryHeap[] memoryHeaps;
    }

    private delegate int VkGetMemoryWin32HandleKHRProc(IntPtr device, ref VkMemoryGetWin32HandleInfoKHR info, out IntPtr handle);

    private const string VulkanLib = "vulkan-1.dll"; // This might change one day...?

    [DllImport(VulkanLib)] private static extern int vkCreateImage(IntPtr device, ref VkImageCreateInfo info, IntPtr allocator, out ulong image);
    [DllImport(VulkanLib)] private static extern void vkDestroyImage(IntPtr device, ulong image, IntPtr allocator);
    [DllImport(VulkanLib)] private static extern void vkGetImageMemoryRequirements(IntPtr device, ulong image, out VkMemoryRequirements requirements);
    [DllImport(VulkanLib)] private static extern int vkAllocateMemory(IntPtr device, ref VkMemoryAllocateInfo info, IntPtr allocator, out ulong memory);
    [DllImport(VulkanLib)] private static extern void vkFreeMemory(IntPtr device, ulong memory, IntPtr allocator);
    [DllImport(VulkanLib)] private static extern int vkBindImageMemory(IntPtr device, ulong image, ulong memory, ulong offset);
    [DllImport(VulkanLib)] private static extern void vkGetPhysicalDeviceMemoryProperties(IntPtr physicalDevice, out VkPhysicalDeviceMemoryProperties props);
    [DllImport(VulkanLib)] private static extern IntPtr vkGetDeviceProcAddr(IntPtr device, string name);

    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
