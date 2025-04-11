using Eto.Drawing;

namespace PoeModTool;

using Eto.Forms;

public sealed class MainWindow : Form
{
    public MainWindow()
    {
        Title = "POE Mod Tool";
        Size = new Size(800, 500);
        Resizable = false;

        // 获取屏幕的工作区大小
        var screen = Screen.WorkingArea;
        // 计算窗口的位置，使其居中显示
        Location = new Point((int)(screen.Width - Width) / 2, (int)(screen.Height - Height) / 2);
        
        
    }
}