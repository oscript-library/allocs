// Portions of this code are derived from `BenchmarkDotNet` (https://github.com/dotnet/BenchmarkDotNet),
// Copyright (c) 2013–2024 .NET Foundation and contributors. MIT License.

using System;
using System.Reflection;
using ScriptEngine.Machine.Contexts;

#if NET8_0_OR_GREATER
using OneScript.Contexts;
#endif

namespace AllocsOneScript
{
    [ContextClass("МониторПамяти", "MemoryMonitor")]
    public class MemoryMonitor : AutoContext<MemoryMonitor>
    {
        private decimal _StartAllocatedBytes;

        [ScriptConstructor]
        public static MemoryMonitor Constructor()
        {
            return new MemoryMonitor();
        }

        [ContextMethod("Начать", "Start")]
        public void Start()
        {
            _StartAllocatedBytes = (decimal)GetAllocatedBytes();
        }

        [ContextMethod("Завершить", "Stop")]
        public decimal Stop()
        {
            return Math.Max(0, (decimal)GetAllocatedBytes() - _StartAllocatedBytes);
        }

        [ContextMethod("РазмерКучи", "HeapSize")]
        public decimal HeapSize() => GC.GetTotalMemory(true);

        [ContextMethod("ВсегоВыделеноБайт", "TotalAllocatedBytes")]
        public decimal GetTotalAllocatedBytes() => (decimal)GetAllocatedBytes();

        private long? GetAllocatedBytes()
        {
            // "This instance Int64 property returns the number of bytes that have been allocated by a specific
            // AppDomain. The number is accurate as of the last garbage collection." - CLR via C#
            // so we enforce GC.Collect here just to make sure we get accurate results
            GC.Collect();

#if NET6_0_OR_GREATER
            return GC.GetTotalAllocatedBytes(precise: true);
#else
            if (GcHelpers.GetTotalAllocatedBytesDelegate != null) // it's .NET Core 3.0 with the new API available
                return GcHelpers.GetTotalAllocatedBytesDelegate.Invoke(true); // true for the "precise" argument

            if (GcHelpers.CanUseMonitoringTotalAllocatedMemorySize) // Monitoring is not available in Mono, see http://stackoverflow.com/questions/40234948/how-to-get-the-number-of-allocated-bytes-
                return AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;

            if (GcHelpers.GetAllocatedBytesForCurrentThreadDelegate != null)
                return GcHelpers.GetAllocatedBytesForCurrentThreadDelegate.Invoke();

            return null;
#endif
        }

#if !NET6_0_OR_GREATER
        // Separate class to have the cctor run lazily, to avoid enabling monitoring before the benchmarks are ran.
        private static class GcHelpers
        {
            // do not reorder these, CheckMonitoringTotalAllocatedMemorySize relies on GetTotalAllocatedBytesDelegate being initialized first
            public static readonly Func<bool, long> GetTotalAllocatedBytesDelegate = CreateGetTotalAllocatedBytesDelegate();
            public static readonly Func<long> GetAllocatedBytesForCurrentThreadDelegate = CreateGetAllocatedBytesForCurrentThreadDelegate();
            public static readonly bool CanUseMonitoringTotalAllocatedMemorySize = CheckMonitoringTotalAllocatedMemorySize();

            private static Func<bool, long> CreateGetTotalAllocatedBytesDelegate()
            {
                try
                {
                    // this method is not a part of .NET Standard so we need to use reflection
                    var method = typeof(GC).GetTypeInfo().GetMethod("GetTotalAllocatedBytes", BindingFlags.Public | BindingFlags.Static);

                    if (method == null)
                        return null;

                    // we create delegate to avoid boxing, IMPORTANT!
                    var del = (Func<bool, long>)method.CreateDelegate(typeof(Func<bool, long>));

                    // verify the api works
                    return del.Invoke(true) >= 0 ? del : null;
                }
                catch
                {
                    return null;
                }
            }

            private static Func<long> CreateGetAllocatedBytesForCurrentThreadDelegate()
            {
                try
                {
                    // this method is not a part of .NET Standard so we need to use reflection
                    var method = typeof(GC).GetTypeInfo().GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static);

                    if (method == null)
                        return null;

                    // we create delegate to avoid boxing, IMPORTANT!
                    var del = (Func<long>)method.CreateDelegate(typeof(Func<long>));

                    // verify the api works
                    return del.Invoke() >= 0 ? del : null;
                }
                catch
                {
                    return null;
                }
            }

            private static bool CheckMonitoringTotalAllocatedMemorySize()
            {
                try
                {
                    // we potentially don't want to enable monitoring if we don't need it
                    if (GetTotalAllocatedBytesDelegate != null)
                        return false;

                    // check if monitoring is enabled
                    if (!AppDomain.MonitoringIsEnabled)
                        AppDomain.MonitoringIsEnabled = true;

                    // verify the api works
                    return AppDomain.MonitoringIsEnabled && AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize >= 0;
                }
                catch
                {
                    return false;
                }
            }
        }
#endif

    }
}
