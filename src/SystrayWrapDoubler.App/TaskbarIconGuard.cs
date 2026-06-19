using System.Runtime.InteropServices;

namespace SystrayWrapDoubler;

internal sealed class TaskbarIconGuard : NativeWindow, IDisposable
{
    private readonly Action _taskbarCreated;
    private readonly int _taskbarCreatedMessage;

    public TaskbarIconGuard(Action taskbarCreated)
    {
        _taskbarCreated = taskbarCreated;
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        CreateHandle(new CreateParams());
    }

    public void Dispose()
    {
        DestroyHandle();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == _taskbarCreatedMessage)
        {
            _taskbarCreated();
        }

        base.WndProc(ref m);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string lpString);
}
