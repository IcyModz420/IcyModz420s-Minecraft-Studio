// MinecraftStudio/Phone/Sta.cs: Runs code on its own STA thread (the Windows Shell's COM objects need one).
// Owns: Sta.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MinecraftStudio.Phone;

/// <summary>Runs code on its own STA thread (the Windows Shell's COM objects need one).</summary>
static class Sta
{
    public static T Run<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception e) { error = e; }
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is OperationCanceledException) throw error;
        if (error is IOException) throw error;
        if (error != null) throw new IOException(error.Message, error);
        return result;
    }

    /// <summary>Waits while letting the Shell's copy engine talk to this thread (it may post messages to it).</summary>
    public static void Wait(int ms)
    {
        var until = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < until)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
    }
}
