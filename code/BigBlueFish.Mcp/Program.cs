using System.IO.Pipes;
using BigBlueFish.Control;

try
{
    var pipeName = args.Length == 2 && args[0] == "--pipe" ? args[1] : ControlTools.PipeName;
    using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    await pipe.ConnectAsync(5000);
    using var cancel = new CancellationTokenSource();
    var input = Console.OpenStandardInput(); var output = Console.OpenStandardOutput();
    var toPet = input.CopyToAsync(pipe, cancel.Token); var toAgent = pipe.CopyToAsync(output, cancel.Token);
    await Task.WhenAny(toPet, toAgent);
    cancel.Cancel(); pipe.Dispose();
    try { await toAgent; } catch (OperationCanceledException) { } catch (ObjectDisposedException) { }
}
catch (Exception e)
{
    await Console.Error.WriteLineAsync("蓝色大肥鱼内部工具未连接：请通过标准版的“连接 Agent”页面建立连接。" + e.Message);
    Environment.ExitCode = 1;
}
