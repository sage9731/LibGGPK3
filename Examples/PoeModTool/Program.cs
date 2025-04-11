using Eto.Forms;

namespace PoeModTool;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var app = new Application();
        var form = new MainWindow();
        app.UnhandledException += (o, e) =>
            MessageBox.Show(app.MainForm, e.ExceptionObject.ToString(), "Error", MessageBoxType.Error);
        app.Run(app.MainForm = form);
    }
}