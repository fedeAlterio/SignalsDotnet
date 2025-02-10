using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using R3;

namespace SignalsDotnet.Animations;

public static class ReactiveAnimation
{
    public static Observable<AnimationFrame<double>> WithDuration(TimeSpan duration, FrameProvider? frameProvider = null)
    {
        return Observable.Defer(() =>
        {
            var stopWatch = Stopwatch.StartNew();
            TimeSpan elapsed = TimeSpan.Zero;
            const double from = 0;
            const double to = 1;
            var speed = 1 / duration.TotalMilliseconds;

            AnimationFrame<double> GetAnimationFrame(double previousValue)
            {
                var previousElapsed = elapsed;
                elapsed = stopWatch.Elapsed;
                var deltaTime = elapsed - previousElapsed;
                var newValue = previousValue + speed * deltaTime.TotalMilliseconds;
                return new(newValue, deltaTime);
            }

            stopWatch.Start();

            var everyUpdate = frameProvider is null ? Observable.EveryUpdate() : Observable.EveryUpdate(frameProvider);

            return everyUpdate.Scan(new AnimationFrame<double>(from, TimeSpan.Zero), (x, _) => GetAnimationFrame(x.Value))
                              .TakeUntil(x => x.Value is <= @from or >= to)
                              .Do(onDispose: stopWatch.Stop);
        });
    }

    public static Observable<AnimationFrame<double>> Animate(this Observable<AnimationFrame<double>> @this,
                                                             Action<AnimationFrame<double>> action,
                                                             double from = 0,
                                                             double to = 1,
                                                             AnimationCancellationOptions animationCancellationOptions = AnimationCancellationOptions.KeepLastValue)
    {
        return Observable.Create<AnimationFrame<double>>(observer =>
        {
            Stopwatch? stopWatch = animationCancellationOptions == AnimationCancellationOptions.KeepLastValue
                ? null
                : Stopwatch.StartNew();

            double lastValue = 0;

            var singleAssignmentDisposable = new SingleAssignmentDisposable();
            var disposable = @this.Do(x =>
                                  {
                                      var min = Math.Min(from, to);
                                      var max = Math.Max(from, to);
                                      var value = Math.Clamp(from + x.Value * (to - from), min, max);
                                      lastValue = value;
                                      stopWatch?.Reset();
                                      action(x with { Value = value });
                                  })
                                  .Subscribe(observer.OnNext, observer.OnErrorResume, observer.OnCompleted);

            singleAssignmentDisposable.Disposable = disposable;

            return Disposable.Create(() =>
            {
                try
                {
                    if (animationCancellationOptions is not AnimationCancellationOptions.KeepLastValue)
                    {
                        stopWatch!.Stop();
                        if (lastValue == to)
                        {
                            return;
                        }

                        var elapsed = stopWatch!.Elapsed;
                        var value = animationCancellationOptions is AnimationCancellationOptions.SnapToStart ? from : to;
                        action(new AnimationFrame<double>(value, elapsed));
                    }
                }
                finally
                {
                    singleAssignmentDisposable.Dispose();
                }
            });
        });
    }

    public static Observable<AnimationFrame<double>> Animate(this Observable<AnimationFrame<double>> @this,
                                                             Action<AnimationFrame<Vector4>> action,
                                                             Vector4 from,
                                                             Vector4 to,
                                                             AnimationCancellationOptions animationCancellationOptions = AnimationCancellationOptions.KeepLastValue)
    {
        var deltaPos = to - from;
        return @this.Animate(x =>
        {
            var animationFrame = new AnimationFrame<Vector4>
            {
                DeltaTime = x.DeltaTime,
                Value = from + (float)x.Value * deltaPos
            };

            action(animationFrame);
        }, 0, 1, animationCancellationOptions);
    }


    public static Observable<AnimationFrame<double>> Animate(this Observable<AnimationFrame<double>> @this,
                                                             Action<AnimationFrame<Vector3>> action,
                                                             Vector3 from,
                                                             Vector3 to,
                                                             AnimationCancellationOptions animationCancellationOptions = AnimationCancellationOptions.KeepLastValue)
    {
        var deltaPos = to - from;
        return @this.Animate(x =>
        {
            var animationFrame = new AnimationFrame<Vector3>
            {
                DeltaTime = x.DeltaTime,
                Value = from + (float)x.Value * deltaPos
            };

            action(animationFrame);
        }, 0, 1, animationCancellationOptions);
    }


    public static Observable<AnimationFrame<double>> Animate(this Observable<AnimationFrame<double>> @this,
                                                             Action<AnimationFrame<Vector2>> action,
                                                             Vector2 from,
                                                             Vector2 to,
                                                             AnimationCancellationOptions animationCancellationOptions = AnimationCancellationOptions.KeepLastValue)
    {
        var deltaPos = to - from;
        return @this.Animate(x =>
        {
            var animationFrame = new AnimationFrame<Vector2>
            {
                DeltaTime = x.DeltaTime,
                Value = from + (float)x.Value * deltaPos
            };

            action(animationFrame);
        }, 0, 1, animationCancellationOptions);
    }

    public static Observable<AnimationFrame<double>> Animate(this Observable<AnimationFrame<double>> @this,
                                                             Action<AnimationFrame<Color>> action,
                                                             Color from,
                                                             Color to,
                                                             AnimationCancellationOptions animationCancellationOptions = AnimationCancellationOptions.KeepLastValue)
    {
        return @this.Animate(x =>
        {
            var value = x.Value;
            var animationFrame = new AnimationFrame<Color>
            {
                DeltaTime = x.DeltaTime,
                Value = Color.FromArgb((int)(from.A + (to.A - from.A) * value),
                                       (int)(from.R + (to.R - from.R) * value),
                                       (int)(from.G + (to.G - from.G) * value),
                                       (int)(from.B + (to.B - from.B) * value))
            };

            action(animationFrame);
        }, 0, 1, animationCancellationOptions);
    }
}

public enum AnimationCancellationOptions
{
    KeepLastValue,
    SnapToStart,
    SnapToEnd
}

public record struct AnimationFrame<T>(T Value, TimeSpan DeltaTime);
