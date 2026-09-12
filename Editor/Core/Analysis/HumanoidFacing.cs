namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Resolve an unambiguous quarter-turn from two separated lower legs.
/// Feet select the forward sign; uncertain or already frontal geometry is retained.</summary>
internal static class HumanoidFacing
{
    public static ImportedCharacter Normalize(ImportedCharacter character)
    {
        var body=character.Meshes.Where(m=>m.Kind==MeshKind.Body).SelectMany(m=>m.Vertices).ToArray();
        if(body.Length<100)return character;
        float bottom=body.Min(p=>p.Y),height=body.Max(p=>p.Y)-bottom;
        var legs=body.Where(p=>p.Y>bottom+height*.15f&&p.Y<bottom+height*.35f).Distinct().ToArray();
        if(legs.Length<20)return character;
        var center=Geometry.Mean(legs);
        float xVariance=legs.Average(p=>(p.X-center.X)*(p.X-center.X));
        float zVariance=legs.Average(p=>(p.Z-center.Z)*(p.Z-center.Z));
        if(zVariance<xVariance*3)return character;
        float middle=(BodyDetector.Quantile(legs.Select(p=>p.Z),.1f)+BodyDetector.Quantile(legs.Select(p=>p.Z),.9f))*.5f;
        if(legs.Count(p=>Math.Abs(p.Z-middle)<height*.012f)>legs.Length*.15f)return character;
        var feet=body.Where(p=>p.Y<bottom+height*.07f).ToArray();
        if(feet.Length<8)return character;
        float forward=(BodyDetector.Quantile(feet.Select(p=>p.X),.1f)+BodyDetector.Quantile(feet.Select(p=>p.X),.9f))*.5f-center.X;
        if(Math.Abs(forward)<height*.012f)return character;
        float sign=forward<0?1:-1;
        Vector3 Turn(Vector3 p)=>new(sign*p.Z,p.Y,-sign*p.X);
        return new ImportedCharacter{Name=character.Name,SourcePath=character.SourcePath,SourceUnitCm=character.SourceUnitCm,SourceUpAxis=character.SourceUpAxis,
            HasExistingSkin=character.HasExistingSkin,ExistingBones=character.ExistingBones.Select(b=>b with{Position=Turn(b.Position)}).ToArray(),
            Meshes=character.Meshes.Select(m=>m with{Vertices=m.Vertices.Select(Turn).ToArray(),CornerNormals=m.CornerNormals.Select(Turn).ToArray()}).ToArray(),
            Materials=character.Materials,EmbeddedTextures=character.EmbeddedTextures,
            ImportWarnings=character.ImportWarnings.Append("The character was turned to face forward.").ToArray()};
    }
}
