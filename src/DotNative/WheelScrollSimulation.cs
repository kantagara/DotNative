namespace DotNative;

// Adapted fixed-extent settling and friction equations; see THIRD_PARTY_NOTICES.
internal sealed record WheelScrollSimulation(
    float Target,
    double Duration,
    Func<double, double> Position
)
{
    public double At(float progress) => progress >= 1 ? Target : Position(progress * Duration);

    public static WheelScrollSimulation Create(
        float start,
        float velocity,
        float extent,
        float maximum
    )
    {
        const double drag = 0.135;
        const double endSpeed = 10;
        var speed = Math.Clamp((double)velocity, -8000, 8000);
        var naturalEnd = start - speed / Math.Log(drag);
        var target = (float)Math.Clamp(Math.Floor(naturalEnd / extent + 0.5) * extent, 0, maximum);
        var currentItem = Math.Floor(start / extent + 0.5);
        if (Math.Abs(speed) < endSpeed || Math.Floor(target / extent + 0.5) == currentItem)
            return Bound(Spring(start, target, speed), maximum);

        // Adjust friction so position and velocity reach the chosen row together.
        var distance = target - start;
        var finalVelocity = Math.CopySign(endSpeed, speed);
        var rate = (speed - finalVelocity) / distance;
        if (rate <= 0 || !double.IsFinite(rate))
            return Bound(Spring(start, target, 0), maximum);
        var duration = Math.Log(Math.Abs(speed) / endSpeed) / rate;
        return new(target, duration, time => start + speed / rate * (1 - Math.Exp(-rate * time)));
    }

    private static WheelScrollSimulation Bound(WheelScrollSimulation motion, float maximum) =>
        motion with
        {
            Position = time => Math.Clamp(motion.Position(time), 0, maximum),
        };

    private static WheelScrollSimulation Spring(double start, float target, double velocity)
    {
        const double mass = 0.5,
            stiffness = 100,
            dampingRatio = 1.1;
        var damping = 2 * dampingRatio * Math.Sqrt(mass * stiffness);
        var discriminant = Math.Sqrt(damping * damping - 4 * mass * stiffness);
        var slow = (-damping + discriminant) / (2 * mass);
        var fast = (-damping - discriminant) / (2 * mass);
        var displacement = start - target;
        var a = (velocity - fast * displacement) / (slow - fast);
        var b = displacement - a;
        double Position(double time) =>
            target + a * Math.Exp(slow * time) + b * Math.Exp(fast * time);
        double Speed(double time) =>
            a * slow * Math.Exp(slow * time) + b * fast * Math.Exp(fast * time);
        var duration = 0.016;
        while (
            duration < 3
            && (Math.Abs(Position(duration) - target) > 0.0005 || Math.Abs(Speed(duration)) > 0.01)
        )
            duration += 0.016;
        return new(target, duration, Position);
    }
}
