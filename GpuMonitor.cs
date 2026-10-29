using System;
using System.Runtime.InteropServices;

namespace DeskMonitor
{
    public class GpuMonitor : IDisposable
    {
        [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int PdhOpenQuery(IntPtr szDataSource, IntPtr dwUserData, out IntPtr phQuery);

        [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int PdhAddEnglishCounterW(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

        [DllImport("pdh.dll", SetLastError = true)]
        private static extern int PdhCollectQueryData(IntPtr hQuery);

        [DllImport("pdh.dll", SetLastError = true)]
        private static extern int PdhCloseQuery(IntPtr hQuery);

        [StructLayout(LayoutKind.Sequential)]
        private struct PDH_FMT_COUNTERVALUE_ITEM
        {
            public IntPtr szName;
            public int CStatus;
            public double doubleValue;
        }

        [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int PdhGetFormattedCounterArrayW(IntPtr hCounter, uint dwFormat, ref uint lpdwBufferSize, ref uint lpdwItemCount, IntPtr ItemBuffer);

        private IntPtr _hQuery = IntPtr.Zero;
        private IntPtr _hCounter = IntPtr.Zero;
        private bool _isInitialized = false;
        private readonly int _itemStructSize;

        public GpuMonitor()
        {
            _itemStructSize = Marshal.SizeOf(typeof(PDH_FMT_COUNTERVALUE_ITEM));
            Initialize();
        }

        private void Initialize()
        {
            try
            {
                if (PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out _hQuery) == 0)
                {
                    // Query 3D GPU engine instances which represent active graphics load
                    if (PdhAddEnglishCounterW(_hQuery, "\\GPU Engine(*engtype_3D)\\Utilization Percentage", IntPtr.Zero, out _hCounter) == 0)
                    {
                        PdhCollectQueryData(_hQuery);
                        _isInitialized = true;
                    }
                }
            }
            catch
            {
                _isInitialized = false;
            }
        }

        public double GetGpuUsage()
        {
            if (!_isInitialized || _hQuery == IntPtr.Zero || _hCounter == IntPtr.Zero)
            {
                return 0;
            }

            try
            {
                if (PdhCollectQueryData(_hQuery) != 0) return 0;

                uint bufSize = 0;
                uint itemCount = 0;
                PdhGetFormattedCounterArrayW(_hCounter, 0x00000200 /* PDH_FMT_DOUBLE */, ref bufSize, ref itemCount, IntPtr.Zero);

                if (bufSize == 0 || itemCount == 0) return 0;

                IntPtr pBuf = Marshal.AllocHGlobal((int)bufSize);
                try
                {
                    if (PdhGetFormattedCounterArrayW(_hCounter, 0x00000200, ref bufSize, ref itemCount, pBuf) == 0)
                    {
                        double total = 0;
                        for (int i = 0; i < itemCount; i++)
                        {
                            IntPtr itemPtr = new IntPtr(pBuf.ToInt64() + i * _itemStructSize);
                            PDH_FMT_COUNTERVALUE_ITEM item = (PDH_FMT_COUNTERVALUE_ITEM)Marshal.PtrToStructure(itemPtr, typeof(PDH_FMT_COUNTERVALUE_ITEM));
                            if (item.CStatus == 0 && item.doubleValue > 0)
                            {
                                total += item.doubleValue;
                            }
                        }
                        return Math.Round(Math.Min(100.0, Math.Max(0.0, total)), 1);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pBuf);
                }
            }
            catch { }

            return 0;
        }

        public void Dispose()
        {
            try
            {
                if (_hQuery != IntPtr.Zero)
                {
                    PdhCloseQuery(_hQuery);
                    _hQuery = IntPtr.Zero;
                    _hCounter = IntPtr.Zero;
                }
            }
            catch { }
        }
    }
}
