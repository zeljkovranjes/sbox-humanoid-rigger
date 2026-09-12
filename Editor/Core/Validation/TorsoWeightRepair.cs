namespace HumanoidRigger;
using Vector3 = System.Numerics.Vector3;

/// <summary>Keeps limb motion from pulling the central spine surface sideways.</summary>
public static class TorsoWeightRepair
{
    /// <summary>Keep the lumbar correction only when all existing stress poses remain safe.</summary>
    public static ValidationReport Improve(ImportedCharacter character, GeneratedRig rig, ValidationReport initial)
    {
        var roles = rig.Bones.Select(b => b.Role).ToHashSet();
        var expected = Deformation.Poses.Where(p => Deformation.IsApplicable(p, roles))
            .Select(p => p.Name).Order().ToArray();
        if (!WeightRepair.HasCompleteEvidence(initial, expected)) return initial;
        var original = rig.Weights;
        rig.Weights = original.Select(p => (Influence[][])p.Clone()).ToArray();
        bool accepted = false;
        try
        {
            int changed = Apply(character, rig);
            if (changed == 0) return initial;
            // Redistributing torso weights can expose a fold at the hip boundary.
            // Run the same bounded surface repair used by initial rig generation.
            var candidate = RigValidator.ValidateAndRepair(character, rig);
            if (!candidate.Passed || !WeightRepair.HasCompleteEvidence(candidate, expected) ||
                !candidate.StressTests.Select(t => t.Pose).SequenceEqual(initial.StressTests.Select(t => t.Pose)) ||
                candidate.StressTests.Zip(initial.StressTests).Any(p =>
                    p.First.ReversedTriangles > p.Second.ReversedTriangles ||
                    p.First.ReversedAreaFraction > p.Second.ReversedAreaFraction + 1e-7f)) return initial;
            candidate.Repairs += initial.Repairs + changed;
            candidate.RepairPasses = initial.RepairPasses + 1;
            accepted = true;
            return candidate;
        }
        finally { if (!accepted) rig.Weights = original; }
    }

    public static int Apply(ImportedCharacter character, GeneratedRig rig)
    {
        int Find(string role) => Array.FindIndex(rig.Bones, b => b.Role == role);
        int pelvis = Find("Pelvis"), spine = Find("SpineLower"), left = Find("UpperLeg.L"), right = Find("UpperLeg.R");
        if (pelvis < 0 || spine < 0 || left < 0 || right < 0) return 0;
        var hips = (rig.Bones[left].Position + rig.Bones[right].Position) * .5f;
        float halfWidth = Math.Abs(rig.Bones[left].Position.X - rig.Bones[right].Position.X) * .5f;
        float rise = rig.Bones[spine].Position.Y - hips.Y;
        if (halfWidth < .001f || rise < .001f) return 0;
        var legs = new bool[rig.Bones.Length];
        var arms = new bool[rig.Bones.Length];
        var torso = new bool[rig.Bones.Length];
        int shoulderLeft = Find("UpperArm.L"), shoulderRight = Find("UpperArm.R");
        int chest = Find("Chest"), neck = Find("Neck");
        float shoulderWidth = shoulderLeft < 0 || shoulderRight < 0 ? 0
            : Math.Abs(rig.Bones[shoulderLeft].Position.X - rig.Bones[shoulderRight].Position.X) * .5f;
        bool constrainShoulders = shoulderWidth > .001f && chest >= 0 && neck >= 0;
        for (int b = 0; b < rig.Bones.Length; b++)
        {
            legs[b] = b == left || b == right || rig.Bones[b].Parent >= 0 && legs[rig.Bones[b].Parent];
            arms[b] = rig.Bones[b].Role is "Clavicle.L" or "Clavicle.R" || rig.Bones[b].Parent >= 0 && arms[rig.Bones[b].Parent];
            torso[b] = rig.Bones[b].Role is "Pelvis" or "SpineLower" or "SpineMid" or "Chest" or "Neck";
        }
        static float Smooth(float x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
        int changed = 0;
        for (int part = 0; part < character.Meshes.Length; part++)
        {
            var mesh = character.Meshes[part];
            if (mesh.Kind != MeshKind.Body) continue;
            for (int v = 0; v < mesh.Vertices.Length; v++)
            {
                var point = mesh.Vertices[v];
                float central = 1 - Smooth((Math.Abs(point.X - hips.X) - halfWidth) / halfWidth);
                float maximum = 1 - central * Smooth((point.Y - hips.Y) / rise);
                var source = rig.Weights[part][v];
                var adjusted = Limit(source, legs, maximum, pelvis);
                if (constrainShoulders && point.Y >= rig.Bones[spine].Position.Y && point.Y <= rig.Bones[neck].Position.Y)
                {
                    // The shoulder blades may follow the clavicles; the middle of the
                    // spine must follow the axial skeleton. Fade toward the shoulders
                    // so their attachment remains free to articulate.
                    float centerX = rig.Bones[chest].Position.X;
                    float shoulderLimit = Smooth((Math.Abs(point.X - centerX) / shoulderWidth - .1f) / .6f);
                    adjusted = Limit(adjusted, arms, shoulderLimit, chest);
                }
                if (ReferenceEquals(source, adjusted)) continue;
                rig.Weights[part][v] = adjusted;
                changed++;
            }
        }
        return changed;

        Influence[] Limit(Influence[] source, bool[] limb, float maximum, int fallback)
        {
            float limbTotal = 0, torsoTotal = 0;
            foreach (var w in source)
            {
                if (limb[w.Bone]) limbTotal += w.Weight;
                if (torso[w.Bone]) torsoTotal += w.Weight;
            }
            if (limbTotal <= maximum + .000001f) return source;
            float removed = limbTotal - maximum;
            var adjusted = new Influence[source.Length + (torsoTotal > .000001f ? 0 : 1)];
            for (int i = 0; i < source.Length; i++)
            {
                var w = source[i];
                float factor = limb[w.Bone] ? maximum / limbTotal
                    : torso[w.Bone] && torsoTotal > .000001f ? 1 + removed / torsoTotal : 1;
                adjusted[i] = w with { Weight = w.Weight * factor };
            }
            if (torsoTotal <= .000001f) adjusted[^1] = new(fallback, removed);
            return Skinning.Cleanup(adjusted, rig.Bones.Length, rig.Profile.MaximumInfluences);
        }
    }
}
