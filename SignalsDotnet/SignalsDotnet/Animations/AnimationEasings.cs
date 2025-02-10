using R3;

namespace SignalsDotnet.Animations;

public static class AnimationEasings
{
    public static Observable<AnimationFrame<double>> WithEasingFunction(this Observable<AnimationFrame<double>> @this, Func<double, double> easingFunction)
    {
        return @this.Select(x => x with { Value = easingFunction(x.Value) });
    }

    public static Observable<AnimationFrame<double>> WithEaseInSine(this Observable<AnimationFrame<double>> @this)
    {
        return @this.WithEasingFunction(static x => 1 - Math.Cos(x * Math.PI / 2));
    }

    public static Observable<AnimationFrame<double>> WithEaseOutSine(this Observable<AnimationFrame<double>> @this)
    {
        return @this.WithEasingFunction(static x => Math.Sin((x * Math.PI) / 2));
    }

    public static Observable<AnimationFrame<double>> WithEaseInOutSine(this Observable<AnimationFrame<double>> @this)
    {
        return @this.WithEasingFunction(static x => -(Math.Cos(Math.PI * x) - 1) / 2);
    }

    public static Observable<AnimationFrame<double>> WithEaseInQuad(this Observable<AnimationFrame<double>> @this)
    {
        return @this.WithEasingFunction(static x => x * x);
    }

    public static Observable<AnimationFrame<double>> WithEaseOutQuad(this Observable<AnimationFrame<double>> @this)
    {
        return @this.WithEasingFunction(static x => 1 - (1 - x) * (1 - x));
    }

    public static Observable<AnimationFrame<double>> WithEaseInOutQuad(this Observable<AnimationFrame<double>> @this)
    {
        return @this.WithEasingFunction(static x => x < 0.5
                                                 ? 2 * x * x
                                                 : 1 - Math.Pow(-2 * x + 2, 2) / 2);
    }

    public static Observable<AnimationFrame<double>> WithEaseInCubic(this Observable<AnimationFrame<double>> @this)
    {
        return @this.WithEasingFunction(static x => x * x * x);
    }

    public static Observable<AnimationFrame<double>> WithEaseOutCubic(this Observable<AnimationFrame<double>> @this)
    {
        return @this.WithEasingFunction(static x => 1 - Math.Pow(1 - x, 3));
    }

    public static Observable<AnimationFrame<double>> WithEaseInOutCubic(this Observable<AnimationFrame<double>> @this)
    {
        return @this.WithEasingFunction(static x => x < 0.5
                                                 ? 4 * x * x * x
                                                 : 1 - Math.Pow(-2 * x + 2, 3) / 2);
    }

    public static Observable<AnimationFrame<double>> WithEaseInQuart(this Observable<AnimationFrame<double>> @this)
    {
        return @this.WithEasingFunction(static x => x * x * x * x);
    }

    public static Observable<AnimationFrame<double>> WithEaseOutQuart(this Observable<AnimationFrame<double>> @this)
    {
        return @this.WithEasingFunction(static x => 1 - Math.Pow(1 - x, 4));
    }

    public static Observable<AnimationFrame<double>> WithEaseInOutQuart(this Observable<AnimationFrame<double>> @this)
    {
        return @this.WithEasingFunction(static x => x < 0.5
                                                 ? 8 * x * x * x * x
                                                 : 1 - Math.Pow(-2 * x + 2, 4) / 2);
    }

    public static Observable<AnimationFrame<double>> WithEaseInQuint(this Observable<AnimationFrame<double>> @this)
    {
        return @this.WithEasingFunction(static x => x * x * x * x * x);
    }

    public static Observable<AnimationFrame<double>> WithEaseOutQuint(this Observable<AnimationFrame<double>> @this)
    {
        return @this.WithEasingFunction(static x => 1 - Math.Pow(1 - x, 5));
    }

    public static Observable<AnimationFrame<double>> WithEaseInOutQuint(this Observable<AnimationFrame<double>> @this)
    {
        return @this.WithEasingFunction(static x => x < 0.5
                                                 ? 16 * x * x * x * x * x
                                                 : 1 - Math.Pow(-2 * x + 2, 5) / 2);
    }

    public static Observable<AnimationFrame<double>> WithEaseInExpo(this Observable<AnimationFrame<double>> @this)
    {
        return @this.WithEasingFunction(static x => x == 0 ? 0 : Math.Pow(2, 10 * x - 10));
    }

    public static Observable<AnimationFrame<double>> WithEaseOutExpo(this Observable<AnimationFrame<double>> @this)
    {
        return @this.WithEasingFunction(static x => x == 1 ? 1 : 1 - Math.Pow(2, -10 * x));
    }

    public static Observable<AnimationFrame<double>> WithEaseInOutExpo(this Observable<AnimationFrame<double>> @this)
    {
        return @this.WithEasingFunction(static x => 
                                             x == 0 ? 0 :
                                             x == 1 ? 1 :
                                             x < 0.5 ? Math.Pow(2, 20 * x - 10) / 2
                                             : (2 - Math.Pow(2, -20 * x + 10)) / 2);
    }

    public static Observable<AnimationFrame<double>> WithEaseInCirc(this Observable<AnimationFrame<double>> @this)
    {
        return @this.WithEasingFunction(static x => 1 - Math.Sqrt(1 - Math.Pow(x, 2)));
    }

    public static Observable<AnimationFrame<double>> WithEaseOutCirc(this Observable<AnimationFrame<double>> @this)
    {
        return @this.WithEasingFunction(static x => Math.Sqrt(1 - Math.Pow(x - 1, 2)));
    }

    public static Observable<AnimationFrame<double>> WithEaseInOutCirc(this Observable<AnimationFrame<double>> @this)
    {
        return @this.WithEasingFunction(static x =>
                                             x < 0.5
                                                 ? (1 - Math.Sqrt(1 - Math.Pow(2 * x,  2))) / 2
                                                 : (Math.Sqrt(1 - Math.Pow(-2 * x + 2, 2)) + 1) / 2);
    }

    public static Observable<AnimationFrame<double>> WithEaseInBack(this Observable<AnimationFrame<double>> @this)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;

        return @this.WithEasingFunction(static x => c3 * x * x * x - c1 * x * x);
    }

    public static Observable<AnimationFrame<double>> WithEaseOutBack(this Observable<AnimationFrame<double>> @this)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;

        return @this.WithEasingFunction(static x => 1 + c3 * Math.Pow(x - 1, 3) + c1 * Math.Pow(x - 1, 2));
    }

    public static Observable<AnimationFrame<double>> WithEaseInOutBack(this Observable<AnimationFrame<double>> @this)
    {
        const double c1 = 1.70158;
        const double c2 = c1 * 1.525;

        return @this.WithEasingFunction(static x => x < 0.5
                                                 ? (Math.Pow(2 * x, 2) * ((c2 + 1) * 2 * x - c2)) / 2
                                                 : (Math.Pow(2 * x - 2, 2) * ((c2 + 1) * (x * 2 - 2) + c2) + 2) / 2);
    }

    public static Observable<AnimationFrame<double>> WithEaseInElastic(this Observable<AnimationFrame<double>> @this)
    {
        const double c4 = (2 * Math.PI) / 3;

        return @this.WithEasingFunction(static x => x == 0 ? 0
                                                 : x == 1 ? 1
                                                 : -Math.Pow(2, 10 * x - 10) * Math.Sin((x * 10 - 10.75) * c4));
    }

    public static Observable<AnimationFrame<double>> WithEaseOutElastic(this Observable<AnimationFrame<double>> @this)
    {
        const double c4 = (2 * Math.PI) / 3;

        return @this.WithEasingFunction(static x => x switch
        {
            0 => 0,
            1 => 1,
            _ => Math.Pow(2, -10 * x) * Math.Sin((x * 10 - 0.75) * c4) + 1
        });
    }

    public static Observable<AnimationFrame<double>> WithEaseInOutElastic(this Observable<AnimationFrame<double>> @this)
    {
        const double c5 = (2 * Math.PI) / 4.5;

        return @this.WithEasingFunction(static x => x == 0 ? 0
                                                 : x == 1 ? 1
                                                 : x < 0.5 ? -(Math.Pow(2, 20 * x - 10) * Math.Sin((20 * x - 11.125) * c5)) / 2
                                                 : (Math.Pow(2,  -20 * x + 10) * Math.Sin((20 * x - 11.125) * c5)) / 2 + 1);
    }

    public static Observable<AnimationFrame<double>> WithEaseOutBounce(this Observable<AnimationFrame<double>> @this)
    {
        return @this.WithEasingFunction(EaseOutBounceFunction);
    }

    static double EaseOutBounceFunction(double x)
    {
        const double n1 = 7.5625;
        const double d1 = 2.75;
        return x switch
        {
            < 1 / d1 => n1 * x * x,
            < 2 / d1 => n1 * (x -= 1.5 / d1) * x + 0.75,
            < 2.5 / d1 => n1 * (x -= 2.25 / d1) * x + 0.9375,
            _ => n1 * (x -= 2.625 / d1) * x + 0.984375
        };
    }

    public static Observable<AnimationFrame<double>> WithEaseInOutBounce(this Observable<AnimationFrame<double>> @this)
    {
        return @this.WithEasingFunction(static x => x < 0.5
                                                 ? (1 - EaseOutBounceFunction(1 - 2 * x)) / 2
                                                 : (1 + EaseOutBounceFunction(2 * x - 1)) / 2);
    }

    public static Observable<AnimationFrame<double>> WithEaseInBounce(this Observable<AnimationFrame<double>> @this)
    {
        return @this.WithEasingFunction(x => 1 - EaseOutBounceFunction(x));
    }
}
