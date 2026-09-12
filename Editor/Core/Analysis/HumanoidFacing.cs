namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Resolve up from body geometry, then an unambiguous quarter-turn
/// from separated lower legs. Feet select the forward sign.</summary>
internal static class HumanoidFacing
{
    public static ImportedCharacter Normalize(ImportedCharacter character)
    {
        var up=HumanoidUp.Find(character);
        if(up!=Vector3.UnitY)
        {
            Vector3 Upright(Vector3 p)=>up==Vector3.UnitX?new(-p.Y,p.X,p.Z)
                :up==-Vector3.UnitX?new(p.Y,-p.X,p.Z)
                :up==-Vector3.UnitY?new(p.X,-p.Y,-p.Z)
                :up==Vector3.UnitZ?new(p.X,p.Z,-p.Y):new(p.X,-p.Z,p.Y);
            character=Reorient(character,Upright,"The character's up direction was corrected from its body geometry.");
        }
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
        forward=SeparatedFeetForward(character,bottom,height,middle)??forward;
        if(Math.Abs(forward)<height*.012f)return character;
        float sign=forward<0?1:-1;
        Vector3 Turn(Vector3 p)=>new(sign*p.Z,p.Y,-sign*p.X);
        return Reorient(character,Turn,"The character was turned to face forward.");
    }
    static float? SeparatedFeetForward(ImportedCharacter character,float bottom,float height,float middle)
    {
        // Long hands can reach the floor. If the lower slice contains more than
        // two limbs, follow the inner calf surfaces down to their own feet.
        var origin=(character.Minimum+character.Maximum)*.5f;origin.Y=bottom+height*.25f;
        var sections=MeshSections.Cut(character,origin,Vector3.UnitY,(character.Maximum-character.Minimum).Length(),height*1e-5f);
        if(sections.Length<=2)return null;
        var left=sections.Where(s=>s.Center.Z>middle+height*.025f).OrderBy(s=>s.Center.Z).FirstOrDefault();
        var right=sections.Where(s=>s.Center.Z<middle-height*.025f).OrderByDescending(s=>s.Center.Z).FirstOrDefault();
        if(left is null||right is null)return null;
        var mesh=Geometry.Merge(character.Meshes.Where(m=>m.Kind==MeshKind.Body));
        var neighbors=Geometry.Neighbors(mesh,height*1e-5f);var visited=new bool[mesh.Vertices.Length];var queue=new Queue<int>();
        foreach(var section in new[]{left,right})
        {
            int seed=-1;float distance=float.PositiveInfinity;
            for(int i=0;i<mesh.Vertices.Length;i++)
            {
                float candidate=Vector3.DistanceSquared(mesh.Vertices[i],section.Center);
                if(candidate<distance){seed=i;distance=candidate;}
            }
            if(seed<0||distance>height*height*.08f*.08f)return null;
            if(!visited[seed]){visited[seed]=true;queue.Enqueue(seed);}
        }
        while(queue.TryDequeue(out int vertex))foreach(int next in neighbors[vertex])
            if(!visited[next]&&mesh.Vertices[next].Y<bottom+height*.4f){visited[next]=true;queue.Enqueue(next);}
        var feet=mesh.Vertices.Where((p,i)=>visited[i]&&p.Y<bottom+height*.07f).Select(p=>p.X).ToArray();
        if(feet.Length<8)return null;
        return(BodyDetector.Quantile(feet,.1f)+BodyDetector.Quantile(feet,.9f))*.5f-(left.Center.X+right.Center.X)*.5f;
    }
    static ImportedCharacter Reorient(ImportedCharacter character,Func<Vector3,Vector3> turn,string warning)
    {
        return new ImportedCharacter{Name=character.Name,SourcePath=character.SourcePath,SourceUnitCm=character.SourceUnitCm,SourceUpAxis=character.SourceUpAxis,
            HasExistingSkin=character.HasExistingSkin,ExistingBones=character.ExistingBones.Select(b=>b with{Position=turn(b.Position)}).ToArray(),
            Meshes=character.Meshes.Select(m=>m with{Vertices=m.Vertices.Select(turn).ToArray(),CornerNormals=m.CornerNormals.Select(turn).ToArray()}).ToArray(),
            Materials=character.Materials,EmbeddedTextures=character.EmbeddedTextures,
            ImportWarnings=character.ImportWarnings.Append(warning).ToArray()};
    }
}
