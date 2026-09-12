#nullable enable annotations
using System.Numerics;
using System.Threading.Tasks;
namespace HumanoidRigger;
using Vector3 = System.Numerics.Vector3;

public sealed record ValidationIssue(string Code,string Message,bool Error);
public sealed record StressResult(string Pose,float MaximumStretch,float MinimumAreaRatio,int NonFiniteVertices,float SourceEdgeLength=0,float DeformedEdgeLength=0,int NonFiniteMeasurements=0,int ReversedTriangles=0,float ReversedAreaFraction=0);
public sealed class ValidationReport
{
    public List<ValidationIssue> Issues {get;}=[];
    public List<StressResult> StressTests {get;}=[];
    public int Repairs {get;set;}
    public int RepairPasses {get;set;}
    public bool Passed=>Issues.All(i=>!i.Error) && StressTests.Count>0;
}
public static class RigValidator
{
    public static ValidationReport ValidateAndRepair(ImportedCharacter character,GeneratedRig rig)
        =>ValidateAndRepair(character,rig,new ValidationGeometry(character));
    internal static ValidationReport ValidateAndRepair(ImportedCharacter character,GeneratedRig rig,ValidationGeometry geometry)
    {
        int repairs=0;
        foreach(var part in rig.Weights) for(int v=0;v<part.Length;v++)
        {
            var cleaned=Skinning.Cleanup(part[v],rig.Bones.Length,rig.Profile.MaximumInfluences);
            if(!cleaned.SequenceEqual(part[v])) {part[v]=cleaned;repairs++;}
        }
        var report=Validate(character,rig,null,geometry);report.Repairs=repairs;
        return SurfaceRepair.Improve(character,rig,WeightRepair.Improve(character,rig,report,geometry),geometry);
    }
    public static ValidationReport Validate(ImportedCharacter character,GeneratedRig rig)
        =>Validate(character,rig,null);

    internal static ValidationReport Validate(ImportedCharacter character,GeneratedRig rig,BindTriangle[][]? faces)
        =>Validate(character,rig,faces,null);
    internal static ValidationReport Validate(ImportedCharacter character,GeneratedRig rig,BindTriangle[][]? faces,ValidationGeometry? geometry)
    {
        var height=geometry?.Height??character.AnatomicalHeight;var r=new ValidationReport();void Error(string code,string text)=>r.Issues.Add(new(code,text,true));
        try{rig.Profile.Validate();}catch(Exception e){Error("profile",e.Message);return r;}
        var roles=new HashSet<string>();var names=new HashSet<string>();
        var body=geometry?.Body??character.Meshes.Where(m=>m.Kind==MeshKind.Body).SelectMany(m=>m.Vertices).ToArray();
        for(int i=0;i<rig.Bones.Length;i++)
        {
            var b=rig.Bones[i];
            if(!roles.Add(b.Role)||!names.Add(b.Name))Error("duplicate","Duplicate bone assignment.");
            bool validParent=b.Parent<i&&b.Parent>=-1;
            if(!validParent)Error("hierarchy","Invalid skeleton hierarchy.");
            if(!Geometry.Finite(b.Position)||!float.IsFinite(b.Rotation.LengthSquared())||Math.Abs(b.Rotation.LengthSquared()-1)>.001f)Error("frame","Invalid bone orientation or position.");
            if(b.Deform&&body.Length>0&&(geometry?.JointDistanceSquared(b.Position)??body.Min(p=>Vector3.DistanceSquared(p,b.Position)))>height*height*.15f*.15f)
                Error("joint-placement",$"{b.Role} is too far from the character. Check its landmark.");
            var definition=rig.Profile.Bones.FirstOrDefault(d=>d.Role==b.Role);
            if(definition is null || definition.Name!=b.Name || !validParent || (b.Parent<0 ? null : rig.Bones[b.Parent].Role)!=definition.Parent)Error("mapping","Skeleton differs from the selected profile.");
        }
        foreach(var b in rig.Profile.Bones.Where(b=>b.Required))if(!roles.Contains(b.Role))Error("required",$"Missing {b.Role}.");
        if(rig.Weights.Length!=character.Meshes.Length){Error("weights","Missing mesh skinning.");return r;}
        for(int p=0;p<rig.Weights.Length;p++)
        {
            if(rig.Weights[p].Length!=character.Meshes[p].Vertices.Length){Error("weights","Skinning vertex count mismatch.");continue;}
            foreach(var vertex in rig.Weights[p])
            {
                if(vertex.Length==0||vertex.Length>rig.Profile.MaximumInfluences)Error("influences","Invalid influence count.");
                if(vertex.Any(i=>i.Bone<0||i.Bone>=rig.Bones.Length||!float.IsFinite(i.Weight)||i.Weight<=0))Error("influences","Invalid weight or bone index.");
                if(Math.Abs(vertex.Sum(i=>i.Weight)-1)>.0001f)Error("normalization","Weights do not sum to one.");
            }
        }
        if(r.Issues.Any(i=>i.Error))return r;
        var ends=RigGeometry.SegmentEnds(rig);
        var locality=new SkinningLocality(character,rig,ends);
        for(int p=0;p<character.Meshes.Length;p++)
        {
            var mesh=character.Meshes[p];if(mesh.Kind==MeshKind.Accessory)continue;
            bool remote=false;
            for(int v=0;v<mesh.Vertices.Length&&!remote;v++)
                foreach(var influence in rig.Weights[p][v])
                {
                    if(!rig.Bones[influence.Bone].Deform){Error("nondeforming-influence","Skinning references a non-deforming bone.");remote=true;break;}
                    if(influence.Weight>.05f&&Vector3.Distance(mesh.Vertices[v],Geometry.ClosestOnSegment(mesh.Vertices[v],rig.Bones[influence.Bone].Position,ends[influence.Bone]))>locality.Limit(influence.Bone,mesh.Vertices[v]))
                    {Error("weight-region",$"Mesh '{mesh.Name}' is influenced by a distant anatomical region ({rig.Bones[influence.Bone].Role}).");remote=true;break;}
                }
        }
        if(r.Issues.Any(i=>i.Error))return r;
        faces??=geometry?.Faces??character.Meshes.Select(BindTriangle.Measure).ToArray();
        var specifications=Deformation.Poses.ToArray();
        var tests=new StressResult[specifications.Length];
        Vector3[][] Buffers()=>character.Meshes.Select(m=>new Vector3[m.Vertices.Length]).ToArray();
        void Measure(int i,Vector3[][] buffer)
        {
            if(Deformation.IsApplicable(specifications[i],roles))tests[i]=MeasurePose(character,rig,specifications[i],faces,buffer,height);
        }
        // Poses read the same frozen weights and write separate buffers. Keep
        // report order and each pose's arithmetic serial and deterministic.
        int workers=character.Meshes.Sum(m=>m.Vertices.Length)>=8192?Math.Min(4,Math.Max(1,Environment.ProcessorCount/2)):1;
        if(workers==1)
        {
            var buffer=Buffers();for(int i=0;i<specifications.Length;i++)Measure(i,buffer);
        }
        else Parallel.For(0,specifications.Length,new ParallelOptions{MaxDegreeOfParallelism=workers},Buffers,
            (i,_,buffer)=>{Measure(i,buffer);return buffer;},_=>{});
        for(int i=0;i<specifications.Length;i++)
        {
            var pose=specifications[i];var test=tests[i];
            if(test is null){r.Issues.Add(new("optional-pose",$"{pose.Name}: optional joints absent.",false));continue;}
            r.StressTests.Add(test);
            if(test.ReversedTriangles>0)r.Issues.Add(new("surface-reversal",$"{pose.Name}: {test.ReversedTriangles} surface triangles reverse orientation.",false));
            if(test.NonFiniteVertices>0||test.NonFiniteMeasurements>0)Error("deformation",$"{pose.Name}: deformation produced non-finite coordinates or measurements.");
            else if(test.MaximumStretch>4||test.MinimumAreaRatio<.025f)Error("deformation",$"{pose.Name}: unsafe deformation (stretch {test.MaximumStretch:F2}, area ratio {test.MinimumAreaRatio:F3}).");
        }
        return r;
    }
    internal static StressResult MeasurePose(ImportedCharacter character,GeneratedRig rig,StressPose pose,BindTriangle[][] faces,Vector3[][] deformed,float height)
    {
        var transforms=Deformation.BoneTransforms(rig,Deformation.JointRotations(rig,pose));
        var rotations=transforms.Rotations;
        Deformation.ApplyTransforms(character,rig,transforms.Positions,rotations,deformed);
        float stretch=1,minArea=1,sourceEdge=0,deformedEdge=0;int nonFinite=0,invalidMeasurements=0;
        int reversed=0;double surfaceArea=0,reversedArea=0;
        for(int p=0;p<character.Meshes.Length;p++)
        {
            var mesh=character.Meshes[p];var dst=deformed[p];nonFinite+=dst.Count(v=>!Geometry.Finite(v));
            foreach(var face in faces[p])
            {
                var i=face.A;var j=face.B;var k=face.C;
                var normal=face.Normal;
                var posedNormal=Vector3.Cross(dst[j]-dst[i],dst[k]-dst[i]);
                float area=face.Area,posedArea=posedNormal.Length();
                if(!float.IsFinite(area)||!float.IsFinite(posedArea))invalidMeasurements++;
                else if(area>height*height*1e-10f)
                {
                    float ratio=posedArea/area;
                    if(float.IsFinite(ratio))minArea=Math.Min(minArea,ratio);else invalidMeasurements++;
                    surfaceArea+=area;
                    float alignment=SurfaceOrientation.Alignment(normal,posedNormal,rig.Weights[p][i],rig.Weights[p][j],rig.Weights[p][k],rotations);
                    if(alignment<SurfaceOrientation.ReversalLimit){reversed++;reversedArea+=area;}
                }
                for(int edge=0;edge<3;edge++)
                {
                    var (a,b,length)=face.Edge(edge);var posedLength=Vector3.Distance(dst[a],dst[b]);
                    if(!float.IsFinite(length)||!float.IsFinite(posedLength)){invalidMeasurements++;continue;}
                    if(length<=height*1e-6f)continue;
                    float ratio=posedLength/length;
                    if(!float.IsFinite(ratio)){invalidMeasurements++;continue;}
                    if(ratio>stretch){stretch=ratio;sourceEdge=length;deformedEdge=posedLength;}
                }
            }
        }
        return new(pose.Name,stretch,minArea,nonFinite,sourceEdge,deformedEdge,invalidMeasurements,reversed,surfaceArea>0?(float)(reversedArea/surfaceArea):0);
    }
}
