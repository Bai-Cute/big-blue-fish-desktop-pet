using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
    DateTime nextTestStatus;
    async Task TestCommands()
    {
        if (!App.Args.Contains("--test-mode")) return;
        if (DateTime.UtcNow >= nextTestStatus) {
            nextTestStatus = DateTime.UtcNow.AddSeconds(1);
            File.WriteAllText("test-status.json", JsonSerializer.Serialize(new { version = typeof(LinuxPetWindow).Assembly.GetName().Version!.ToString(3), currentAction, phase, frames = player.Frames, player.LastReadMs, player.LastUiMs, player.LastCopyMs, player.LastShapeMs, text = bubbleText.Text, bubbleVisible = bubbleWindow.IsVisible, Puppet, McpAvailable, visible = IsVisible, hidden, fullscreenHidden, modelPid = brain.RunnerPid, modelBusy = brain.Busy, modelStatus = brain.Status, generating, settingsOpen = settings != null, Position.X, Position.Y }));
        }
        if (!File.Exists("test-command.txt")) return;
        string command = File.ReadAllText("test-command.txt").Trim(); File.Delete("test-command.txt");
        try {
            switch (command)
            {
                case "settings": OpenSettings(); break;
                case "close-settings": settings?.Close(); break;
                case "speech-test": await Speak(false); break;
                case "weather": await Speak(true); break;
                case "hide": hidden = true; Hide(); bubbleWindow.Hide(); break;
                case "show": RestorePet(); break;
                case "close": Close(); break;
                case "exit": CloseImmediately(); break;
                case "ocr":
                    var ocr = await CompanionOcr.ReadAsync(CompanionNative.ResolveForeground(CompanionNative.GetForegroundWindow(), lastForeground), CancellationToken.None);
                    File.WriteAllText("ocr-status.json", JsonSerializer.Serialize(ocr)); break;
                case "render": RenderControl(this, "pet.png"); break;
                case "render-settings": if (settings != null) RenderControl(settings, "settings.png"); break;
#if !COMPANION_SAFE
                case "render-input": if (mcpInput != null) RenderControl(mcpInput, "input.png"); break;
#endif
                default: if (command.StartsWith("bubble ")) ShowText(command[7..], true); break;
            }
        } catch (Exception error) { File.WriteAllText("test-error.txt", error.ToString()); }
    }
    static void RenderControl(Avalonia.Controls.Control control, string path)
    {
        using var image = new RenderTargetBitmap(new PixelSize((int)control.Bounds.Width, (int)control.Bounds.Height)); image.Render(control); image.Save(path, PngBitmapEncoderOptions.Default);
    }
}


