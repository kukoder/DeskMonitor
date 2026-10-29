using System;
using System.Threading;
using System.Windows.Forms;

namespace DeskMonitor
{
    static class Program
    {
        private const string MutexName = "DeskMonitor_SingleInstance_Mutex_99218";

        [STAThread]
        static void Main()
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // Already running - avoid duplicate background processes
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

                try
                {
                    AppSettings settings = AppSettings.Load();
                    MainForm mainForm = new MainForm(settings);
                    Application.Run(mainForm);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка при работе программы: " + ex.Message, "DeskMonitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    try
                    {
                        mutex.ReleaseMutex();
                    }
                    catch { }
                }
            }
        }
    }
}
