using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VPet_Simulator.Windows;
public partial class MainWindow
{
    private CompanionAnimationPolicy animationPolicy = null!;
    private CompanionVideoPlayer? videoPlayer;
    private Image? videoImage;
    private string animationPhase = "Startup";
    private CompanionAnimationRule? movementRule;
    private Point movementOrigin;
    private double movementEnd, movementStart;
    private bool movementMirrored;
    private bool exitAnimation, exitReady;
    private static string AnimationDirectory => Path.Combine(AppContext.BaseDirectory, "assets", "fish");
    internal Rect CompanionVisibleBounds => new(
        (Width - SpriteSize * 640 / 360) / 2 + SpriteSize * 208 / 360,
        Height - SpriteSize + SpriteSize * 58 / 360,
        SpriteSize * 220 / 360, SpriteSize * 278 / 360);

    private void StartVideoAnimations()
    {
        animationPolicy = CompanionAnimationPolicy.Load(AnimationDirectory);
        videoImage = new CompanionSpriteImage { Width = SpriteSize * 640 / 360, Height = SpriteSize,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Stretch = Stretch.Fill };
        companionLayout.Children.Insert(0, videoImage);
        videoPlayer = new(videoImage);
        videoPlayer.Failed += error =>
        {
            File.WriteAllText("companion-errors.log", "动画播放失败：" + error);
            if (exitAnimation) { exitReady = true; base.Close(); return; }
            companionReady = false;
            ShowCompanionNotice("动画未能播放，请检查动画组件是否完整。");
        };
        videoPlayer.FramePresented += MoveWithAnimation;
        companionReady = true;
        PlayAnimation(animationPolicy.Find("女仆屈膝礼仪"), "Startup");
        File.WriteAllText("ready.status", "ready");
    }
    private CompanionAnimationContext AnimationContext() => new(DateTime.Now,
        WorkRecognitionEnabled: false, BreakfastHour: preferences.BreakfastHour,
        LunchHour: preferences.LunchHour, DinnerHour: preferences.DinnerHour);
    private void PlayAnimation(CompanionAnimationRule rule, string phase, bool mirror = false)
    {
        if (!companionReady || companionClosed) return;
        currentAction = rule.Id; animationPhase = phase;
        videoPlayer!.Play(Path.Combine(AnimationDirectory, rule.File), mirror, AnimationFinished);
    }
    private void PlayIdle() => PlayAnimation(animationPolicy.Choose("Idle", AnimationContext(), companionRandom), "Idle");
    private void AnimationFinished()
    {
        if (companionClosed) return;
        if (exitAnimation) { exitReady = true; base.Close(); return; }
        if (animationPhase == "Drag") return; // Hold the final suspended pose until mouse release.
        if (animationPhase == "Outbound" && movementRule != null)
        {
            Left = movementEnd;
            PlayAnimation(animationPolicy.Choose("Idle", AnimationContext(), companionRandom), "BetweenMoves");
            return;
        }
        if (animationPhase == "BetweenMoves" && movementRule != null)
        {
            movementStart = Left; movementEnd = movementOrigin.X;
            PlayAnimation(movementRule, "Return", !movementMirrored);
            return;
        }
        if (animationPhase == "Return") { Left = movementOrigin.X; movementRule = null; anchor = new(Left, Top); }
        if (animationPhase == "Startup") nextAction = DateTime.UtcNow.AddSeconds(preferences.AnimationIntervalSeconds);
        AnimationBoundary();
    }
    private void AnimationBoundary()
    {
        if (exitAnimation || dragging || hiddenByUser || fullscreenHidden || companionSettings != null) { PlayIdle(); return; }
        var local = DateTime.Now;
        var date = local.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (local.Hour == 23 && preferences.LastBrushDate != date)
        {
            preferences.LastBrushDate = date;
            SaveCompanion();
            PlayAnimation(animationPolicy.Find("晨间刷牙"), "Daily");
            return;
        }
        if (DateTime.UtcNow >= nextAction)
        {
            nextAction = DateTime.UtcNow.AddSeconds(preferences.AnimationIntervalSeconds);
            var rule = animationPolicy.Choose("Event", AnimationContext(), companionRandom);
            StartAnimationEvent(rule);
            return;
        }
        PlayIdle();
    }
    private void StartAnimationEvent(CompanionAnimationRule rule)
    {
        if (rule.MoveKind != null)
        {
            movementRule = rule; movementOrigin = new(Left, Top); movementStart = Left;
            // Pick a nearby destination using the visible body bounds, then restore the origin.
            Left -= 70 * preferences.Scale; ClampCompanion(); movementEnd = Left;
            if (Math.Abs(movementEnd - movementStart) < 15)
            { Left = movementStart + 70 * preferences.Scale; ClampCompanion(); movementEnd = Left; }
            Left = movementStart; movementMirrored = movementEnd > movementStart;
            PlayAnimation(rule, "Outbound", movementMirrored);
        }
        else PlayAnimation(rule, "Event");
    }
    private void MoveWithAnimation(double seconds)
    {
        if (movementRule == null || animationPhase is not ("Outbound" or "Return")) return;
        double lead = movementRule.MoveKind == "Run" ? 1.75 : 2;
        double tail = movementRule.MoveKind == "Run" ? 4.8 : 2;
        var fraction = Math.Clamp((seconds - lead) / (10 - lead - tail), 0, 1);
        Left = movementStart + (movementEnd - movementStart) * fraction;
    }
    private void CancelMovement() { movementRule = null; anchor = new(Left, Top); }
    private void ClickAnimation()
    {
        if (!companionReady || exitAnimation) return;
        CancelMovement();
        PlayAnimation(animationPolicy.Choose("Click", AnimationContext(), companionRandom), "Click");
    }
    private void FinishDragAnimation()
    {
        CancelMovement();
        if (companionReady && !exitAnimation) PlayIdle();
    }
    private void CompanionClosing(object? sender, CancelEventArgs e)
    {
        if (exitReady || !companionReady || companionClosed) return;
        e.Cancel = true;
        if (exitAnimation) return;
        exitAnimation = true; CancelMovement();
        speechRequestVersion++;
        dragging = false; ReleaseMouseCapture();
        companionSettings?.Close();
        brain.CancelGeneration(); EndModelSpeech();
        hiddenByUser = false; fullscreenHidden = false;
        // WPF is still inside Closing even when cancellation has been requested.
        // Defer showing a hidden pet until that event has returned.
        if (!IsVisible) Dispatcher.BeginInvoke(() => { if (!companionClosed && !exitReady) Show(); });
        videoPlayer!.Paused = false;
        PlayAnimation(animationPolicy.Find("女仆屈膝礼仪"), "Exit");
    }
}
