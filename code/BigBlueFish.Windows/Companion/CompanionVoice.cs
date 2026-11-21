namespace BigBlueFish.Windows;

// Reserved for 1.2.0. No model, audio engine, background work or settings yet.
// Non-streamed speech arrives at Show; streamed local speech arrives only after
// its final text is accepted. The future implementation can synthesize here and
// cancel playback when the current utterance is dismissed or replaced.
internal static class CompanionVoice
{
    internal static void Queue(string finalText) { }
    internal static void Cancel() { }
}
