namespace HumanoidRigger;
using Vector3 = System.Numerics.Vector3;

/// <summary>Conservative rest-pose checks using both arms, independently of the target profile.</summary>
public static class ImportPose
{
    public const string Warning = "This model does not appear to be in T-Pose, A-Pose 1, or A-Pose 2. Detection is more reliable in one of these poses. You can continue, but check the landmarks carefully.";

    public static bool IsRecommended(Anatomy anatomy)
    {
        float[] angles = new float[2];
        int index = 0;
        foreach (var (side, sign) in new[] { ("L", 1f), ("R", -1f) })
        {
            var shoulder = anatomy["UpperArm." + side];
            var elbow = anatomy["LowerArm." + side];
            var wrist = anatomy["Hand." + side];
            var arm = wrist - shoulder;
            float outward = arm.X * sign;
            if (outward <= anatomy.Height * .035f) return false;
            float angle = MathF.Atan2(-arm.Y, outward) * 180 / MathF.PI;
            if (angle < -15 || angle > 57) return false;
            if (Math.Abs(arm.Z) > arm.Length() * .35f) return false;
            var upper = elbow - shoulder;
            var lower = wrist - elbow;
            if (upper.LengthSquared() < 1e-8f || lower.LengthSquared() < 1e-8f ||
                Vector3.Dot(Vector3.Normalize(upper), Vector3.Normalize(lower)) < .85f) return false;
            angles[index++] = angle;
        }
        return Math.Abs(angles[0] - angles[1]) <= 20;
    }
}
