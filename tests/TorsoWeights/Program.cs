using HumanoidRigger;
using System.Numerics;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }

(ImportedCharacter Character, GeneratedRig Rig) Fixture(float scale = 1, MeshKind kind = MeshKind.Body)
{
    var character = new ImportedCharacter
    {
        Meshes = [new("lumbar", new[] { new Vector3(0, 113, -10), new(1, 114, -10), new(-1, 114, -10) }.Select(p => p * scale).ToArray(), [0, 1, 2], kind)]
    };
    var bones = new[]
    {
        new RigBone("Pelvis", "pelvis", -1, new(0, 100, 0), Quaternion.Identity, true),
        new RigBone("SpineLower", "spine", 0, new(0, 112, 0), Quaternion.Identity, true),
        new RigBone("UpperLeg.L", "thigh_l", 0, new(9, 92, 0), Quaternion.Identity, true),
        new RigBone("UpperLeg.R", "thigh_r", 0, new(-9, 92, 0), Quaternion.Identity, true),
        new RigBone("LowerLeg.L", "calf_l", 2, new(9, 52, 0), Quaternion.Identity, true)
    }.Select(b => b with { Position = b.Position * scale }).ToArray();
    return (character, new GeneratedRig
    {
        Profile = new() { MaximumInfluences = 4 }, Bones = bones,
        Weights = [Enumerable.Range(0, 3).Select(_ => new Influence[] { new(1, .6f), new(0, .3f), new(3, .1f) }).ToArray()]
    });
}

foreach (float scale in new[] { .01f, 1f, 10f })
Test($"Lumbar surface does not follow a stepping leg, scale {scale}", () =>
{
    var (character, rig) = Fixture(scale);
    var motion = new Dictionary<string, Quaternion> { ["UpperLeg.R"] = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 1) };
    var before = Deformation.ApplyRotations(character, rig, motion)[0];
    Check(Vector3.Distance(before[0], character.Meshes[0].Vertices[0]) > scale, "Fixture did not reproduce lumbar pulling.");
    Check(TorsoWeightRepair.Apply(character, rig) == 3, "Lumbar weights were not repaired.");
    var after = Deformation.ApplyRotations(character, rig, motion)[0];
    for (int i = 0; i < after.Length; i++)
    {
        Check(Vector3.Distance(after[i], character.Meshes[0].Vertices[i]) < scale * .0001f, "Leg movement still deforms the spine surface.");
        Check(Math.Abs(rig.Weights[0][i].Sum(w => w.Weight) - 1) < .00001f, "Weights are not normalized.");
        Check(rig.Weights[0][i].Length <= 4, "Influence limit exceeded.");
    }
    Check(TorsoWeightRepair.Apply(character, rig) == 0, "Repair is not idempotent.");
});

Test("Hip transition is continuous and leaves the thigh unchanged", () =>
{
    float previous = 1;
    for (int y = 90; y <= 114; y++)
    {
        var (character, rig) = Fixture();
        character.Meshes[0].Vertices[0] = new(0, y, 0);
        rig.Weights[0][0] = [new(3, .9f), new(0, .1f)];
        TorsoWeightRepair.Apply(character, rig);
        float current = rig.Weights[0][0].Where(w => w.Bone == 3).Sum(w => w.Weight);
        Check(current <= previous + .00001f && previous - current < .101f, "Abrupt hip weight transition.");
        if (y <= 92) Check(Math.Abs(current - .9f) < .00001f, "Thigh weights changed below the hip.");
        previous = current;
    }
});

Test("Accessories retain their authored attachment weights", () =>
{
    var (character, rig) = Fixture(kind: MeshKind.Accessory);
    Check(TorsoWeightRepair.Apply(character, rig) == 0, "Accessory skinning was modified.");
});

Test("Partial rigs without torso landmarks remain unchanged", () =>
{
    var (character, source) = Fixture();
    var rig = new GeneratedRig { Bones = source.Bones.Where(b => b.Role != "SpineLower").ToArray(), Weights = source.Weights };
    Check(TorsoWeightRepair.Apply(character, rig) == 0, "Partial rig was modified.");
});

Test("A distant limb surface is outside the torso constraint", () =>
{
    var (character, rig) = Fixture();
    foreach (ref var p in character.Meshes[0].Vertices.AsSpan()) p.X = 40;
    Check(TorsoWeightRepair.Apply(character, rig) == 0, "Torso constraint reached a distant surface.");
});

Test("Missing torso influences fall back to the pelvis without losing weight", () =>
{
    var (character, rig) = Fixture();
    rig.Weights[0][0] = [new(4, 1)];
    TorsoWeightRepair.Apply(character, rig);
    Check(rig.Weights[0][0].SequenceEqual(new[] { new Influence(0, 1) }), "Descendant leg influence was not moved to the pelvis.");
});
(ImportedCharacter Character, GeneratedRig Rig) ShoulderFixture(float scale = 1)
{
    var (_, seed) = Fixture();
    var bones = seed.Bones.Concat(new[]
    {
        new RigBone("Chest", "chest", 1, new(0, 140, 0), Quaternion.Identity, true),
        new RigBone("Neck", "neck", 5, new(0, 155, 0), Quaternion.Identity, true),
        new RigBone("Clavicle.L", "clavicle_l", 5, new(8, 151, 0), Quaternion.Identity, true),
        new RigBone("UpperArm.L", "arm_l", 7, new(23, 151, 0), Quaternion.Identity, true),
        new RigBone("Clavicle.R", "clavicle_r", 5, new(-8, 151, 0), Quaternion.Identity, true),
        new RigBone("UpperArm.R", "arm_r", 9, new(-23, 151, 0), Quaternion.Identity, true)
    }).Select(b => b with { Position = b.Position * scale }).ToArray();
    return (new ImportedCharacter { Meshes = [new("back", new[] { new Vector3(0, 142, -10), new(1, 143, -10), new(-1, 143, -10) }.Select(p => p * scale).ToArray(), [0, 1, 2], MeshKind.Body)] },
        new GeneratedRig { Profile = seed.Profile, Bones = bones,
            Weights = [Enumerable.Range(0, 3).Select(_ => new Influence[] { new(5, .35f), new(1, .15f), new(7, .35f), new(9, .15f) }).ToArray()] });
}

foreach (float scale in new[] { .01f, 1f, 10f })
Test($"Asymmetric shoulder motion cannot drag the central back, scale {scale}", () =>
{
    var (character, rig) = ShoulderFixture(scale);
    var motion = new Dictionary<string, Quaternion> { ["Clavicle.L"] = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .7f) };
    var before = Deformation.ApplyRotations(character, rig, motion)[0];
    Check(Vector3.Distance(before[0], character.Meshes[0].Vertices[0]) > scale, "Fixture did not reproduce shoulder pulling.");
    Check(TorsoWeightRepair.Apply(character, rig) == 3, "Shoulder influence was not repaired.");
    var after = Deformation.ApplyRotations(character, rig, motion)[0];
    Check(after.Zip(character.Meshes[0].Vertices).All(p => Vector3.Distance(p.First, p.Second) < scale * .0001f), "Shoulder still moves the central back.");
    Check(rig.Weights[0].All(w => w.Length <= 4 && Math.Abs(w.Sum(i => i.Weight) - 1) < .00001f), "Invalid repaired weights.");
    Check(TorsoWeightRepair.Apply(character, rig) == 0, "Shoulder repair is not idempotent.");
});

Test("Shoulder attachment remains mobile with a continuous transition from the spine", () =>
{
    float previous = 0;
    for (int x = 0; x <= 23; x++)
    {
        var (character, rig) = ShoulderFixture();
        character.Meshes[0].Vertices[0] = new(x, 145, -10);
        rig.Weights[0][0] = [new(8, .9f), new(5, .1f)];
        TorsoWeightRepair.Apply(character, rig);
        float current = rig.Weights[0][0].Where(w => w.Bone == 8).Sum(w => w.Weight);
        Check(current >= previous - .00001f && current - previous < .12f, "Abrupt shoulder transition.");
        if (x == 23) Check(Math.Abs(current - .9f) < .00001f, "Shoulder attachment was immobilized.");
        previous = current;
    }
});

Test("Unvalidated skin is not accepted as a safe baseline", () =>
{
    var (character, rig) = Fixture();
    var weights = rig.Weights;
    var report = new ValidationReport();
    Check(ReferenceEquals(TorsoWeightRepair.Improve(character, rig, report), report), "Missing validation was accepted.");
    Check(ReferenceEquals(weights, rig.Weights), "Weights changed without stress coverage.");
});

Test("Validated lumbar correction is retained without moving mesh or bones", () =>
{
    var (source, seed) = Fixture();
    var vertices = source.Meshes[0].Vertices.Concat(seed.Bones.Select(b => b.Position)).ToArray();
    var character = new ImportedCharacter { Meshes = [new("body", vertices, [0, 1, 2], MeshKind.Body)] };
    var rig = new GeneratedRig
    {
        Bones = seed.Bones,
        Profile = new() { Bones = seed.Bones.Select(b => new BoneDefinition(b.Role, b.Name, b.Parent < 0 ? null : seed.Bones[b.Parent].Role)).ToArray() },
        Weights = [Enumerable.Range(0, 3).Select(_ => new Influence[] { new(1, .66f), new(0, .3f), new(3, .04f) })
            .Concat(seed.Bones.Select((_, i) => new Influence[] { new(i, 1) })).ToArray()]
    };
    var positions = vertices.ToArray(); var bones = rig.Bones.ToArray();
    var before = RigValidator.Validate(character, rig);
    Check(before.Passed, "Baseline fixture failed: " + string.Join("; ", before.Issues.Where(i => i.Error)));
    var after = TorsoWeightRepair.Improve(character, rig, before);
    Check(after.Passed && after.Repairs > before.Repairs, "Safe lumbar candidate was not retained.");
    Check(after.StressTests.Count == before.StressTests.Count && after.StressTests.All(t => t.ReversedTriangles == 0), "Stress coverage or safe surface lost.");
    Check(rig.Weights[0].Take(3).All(w => w.All(i => i.Bone != 3)), "Lumbar leg influence survived refinement.");
    Check(vertices.SequenceEqual(positions) && rig.Bones.SequenceEqual(bones), "Repair moved geometry or the skeleton.");
});

Test("Rejected candidate restores the original weights and validation", () =>
{
    var (character, rig) = Fixture();
    var weights = rig.Weights;
    var snapshot = weights[0].Select(w => w.ToArray()).ToArray();
    // Complete baseline measurements, but an invalid profile prevents the proposed
    // weights from receiving independent validation. The edit must be rolled back.
    var report = new ValidationReport();
    var roles = rig.Bones.Select(b => b.Role).ToHashSet();
    foreach (var p in Deformation.Poses.Where(p => Deformation.IsApplicable(p, roles)))
        report.StressTests.Add(new(p.Name, 1, 1, 0));
    Check(ReferenceEquals(TorsoWeightRepair.Improve(character, rig, report), report), "Rejected report replaced baseline.");
    Check(ReferenceEquals(weights, rig.Weights), "Rejected weights replaced baseline.");
    Check(weights[0].Zip(snapshot).All(p => p.First.SequenceEqual(p.Second)), "Trial mutated original influence arrays.");
});
Console.WriteLine($"{passed} passed, 0 failed.");
