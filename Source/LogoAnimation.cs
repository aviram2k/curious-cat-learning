using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Education2k
{
    public static class LogoAnimation
    {
        public class FrameSpec { public string FileName; public int Milliseconds; }
        public class Manifest { public List<FrameSpec> Frames = new List<FrameSpec>(); }
        sealed class AnimationData
        {
            public readonly List<BitmapSource> Frames = new List<BitmapSource>();
            public readonly List<int> Delays = new List<int>();
            public int Duration;
            public AnimationData()
            {
                var assembly = typeof(LogoAnimation).Assembly;
                Manifest manifest;
                using (var stream = assembly.GetManifestResourceStream("Education2k.LogoAnimation.frames.json"))
                using (var reader = new StreamReader(stream)) manifest = Store.Json.Deserialize<Manifest>(reader.ReadToEnd());
                foreach (var frame in manifest.Frames)
                {
                    using (var stream = assembly.GetManifestResourceStream("Education2k.LogoAnimation." + frame.FileName))
                    {
                        var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                        bitmap.Freeze(); Frames.Add(bitmap);
                    }
                    Delays.Add(frame.Milliseconds); Duration += frame.Milliseconds;
                }
            }
        }
        static readonly Lazy<AnimationData> data = new Lazy<AnimationData>(() => new AnimationData());

        public static void Attach(Image image)
        {
            var frames = data.Value;
            image.Source = frames.Frames[0];
            bool running = false;
            Action start = delegate {
                if (running || !image.IsLoaded || !image.IsVisible || !SystemParameters.ClientAreaAnimation) return;
                running = true;
                var animation = new ObjectAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(frames.Duration), RepeatBehavior = RepeatBehavior.Forever };
                int elapsed = 0;
                for (int index = 0; index < frames.Frames.Count; index++)
                {
                    animation.KeyFrames.Add(new DiscreteObjectKeyFrame(frames.Frames[index], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(elapsed))));
                    elapsed += frames.Delays[index];
                }
                Timeline.SetDesiredFrameRate(animation, 30);
                image.BeginAnimation(Image.SourceProperty, animation, HandoffBehavior.SnapshotAndReplace);
            };
            Action stop = delegate { image.BeginAnimation(Image.SourceProperty, null); running = false; };
            image.Loaded += delegate { start(); };
            image.IsVisibleChanged += delegate { if (image.IsVisible) start(); else stop(); };
            // Navigating away releases the animation clock; returning creates a
            // fresh image and restarts the same cached frames without a timer leak.
            image.Unloaded += delegate { stop(); };
        }
    }
}
