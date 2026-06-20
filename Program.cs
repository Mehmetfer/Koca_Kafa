using System;
using System.Windows.Forms;
using Koca_Kafa.Application.DependencyInjection;
using Koca_Kafa.UI;
using SQLitePCL;
using WinFormsApp = System.Windows.Forms.Application;

namespace Koca_Kafa
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Batteries.Init();
            WinFormsApp.EnableVisualStyles();
            WinFormsApp.SetCompatibleTextRenderingDefault(false);

            IAppServiceProvider serviceProvider = CompositionRoot.Build();
            serviceProvider.GetRequiredService<Data.MemoryDatabaseBootstrap>();
            serviceProvider.GetRequiredService<KnowledgeBase.Chroma.ChromaBootstrap>();
            var mainForm = serviceProvider.GetRequiredService<MainForm>();
            WinFormsApp.Run(mainForm);
        }
    }
}
