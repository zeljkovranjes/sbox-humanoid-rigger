#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Centers non-thumb chains on their own closed mesh sections, preserving
/// supported joint spacing and repairing chains collapsed near a fingertip.</summary>
public static class FingerAlignment
{
    public static int Refine(ImportedCharacter character,Anatomy anatomy,string side)
    {
        if(side is not ("L" or "R"))throw new ArgumentException("Unknown hand side.");
        var fingers=new[]{"Index","Middle","Ring","Pinky"};
        var chains=fingers.Select(f=>Enumerable.Range(1,3).Select(i=>f+i+"."+side).Append(f+"Tip."+side).ToArray())
            .Where(roles=>roles.All(r=>anatomy.Points.TryGetValue(r,out var p)&&!p.Corrected&&p.Confidence>=.35f)).ToArray();
        if(chains.Length==0)return 0;
        var wrist=anatomy["Hand."+side];float h=anatomy.Height;
        float reach=chains.SelectMany(r=>r).Max(r=>Vector3.Distance(wrist,anatomy[r]))+h*.035f;
        var local=HandSurface(character,wrist,reach);
        // Containment uses the original complete shells, never the cropped analysis surface.
        var volume=new SurfaceVisibility(character.Meshes.Where(m=>m.Kind==MeshKind.Body),h*.00001f);
        int changed=0;
        foreach(var roles in chains)
        {
            var previous=roles.Select(r=>anatomy[r]).ToArray();var fitted=Fit(local,volume,wrist,previous,h);
            if(fitted is null)continue;
            for(int i=0;i<3;i++)anatomy.Points[roles[i]]=anatomy.Points[roles[i]] with{Position=fitted[i]};
            changed++;
        }
        return changed;
    }
    static Vector3[]? Fit(ImportedCharacter local,SurfaceVisibility volume,Vector3 wrist,Vector3[] previous,float h)
    {
        var tip=previous[3];var inward=wrist-tip;if(inward.LengthSquared()<h*h*.000001f)return null;
        var axis=Vector3.Normalize(inward);var center=tip;float step=h*.0015f;
        var path=new List<Vector3>{tip};var radii=new List<float>();var areas=new List<float>();bool ended=false;
        for(int i=0;i<75;i++)
        {
            var seed=center+axis*step;
            MeshSections.Section? section=null;
            // A plane through a mesh vertex can produce an ambiguous contour.
            // Nearby parallel cuts recover it without changing or welding geometry.
            foreach(float offset in new[]{0f,.15f,-.15f,.35f,-.35f})
            {
                section=MeshSections.Cut(local,seed+axis*(step*offset),axis,h*.035f,h*.00001f)
                    .Where(s=>s.Radius>h*.0007f&&s.Radius<h*.016f&&Vector3.Distance(s.Center,seed)<h*.018f)
                    .MinBy(s=>Vector3.DistanceSquared(s.Center,seed));
                if(section is not null)break;
            }
            if(section is null){ended=true;break;}
            var difference=section.Center-center;
            if(path.Count>1&&difference.Length()>h*.006f){ended=true;break;}
            if(path.Count>5&&section.Area>areas.TakeLast(3).Average()*1.8f){ended=true;break;}
            if(path.Count>2&&volume.Blocked(center,section.Center,h*.00001f)){ended=true;break;}
            if(difference.LengthSquared()<1e-12f)return null;
            var tangent=Vector3.Normalize(difference);
            if(path.Count>1&&Vector3.Dot(axis,tangent)<.3f){ended=true;break;}
            center=section.Center;path.Add(center);radii.Add(section.Radius);areas.Add(section.Area);
            if(path.Count>2)axis=Vector3.Normalize(Vector3.Lerp(axis,tangent,.25f));
            if(Vector3.Distance(center,tip)>inward.Length()*.8f)return null;
        }
        if(path.Count<8||!ended)return null;
        var extension=center+axis*radii[^1]*.5f;
        if(volume.Contains(extension,h*.00001f)&&!volume.Blocked(center,extension,h*.00001f))path.Add(extension);
        path.Reverse();var distances=new float[path.Count];
        for(int i=1;i<path.Count;i++)distances[i]=distances[i-1]+Vector3.Distance(path[i-1],path[i]);
        float coverage=Vector3.Distance(path[0],tip)/Vector3.Distance(previous[0],tip);
        if(coverage<.6f)return null;
        // The webbing can end the trace before a well-supported palm knuckle.
        // Keep that base and still center the distal joints on the recovered digit.
        bool retainBase=coverage<.85f;
        (Vector3 Point,float Offset) Project(Vector3 point)
        {
            float best=float.PositiveInfinity,offset=0;var result=point;
            for(int i=1;i<path.Count;i++)
            {
                var closest=Geometry.ClosestOnSegment(point,path[i-1],path[i]);float distance=Vector3.DistanceSquared(point,closest);
                if(distance>=best)continue;best=distance;result=closest;offset=distances[i-1]+Vector3.Distance(closest,path[i-1]);
            }
            return(result,offset);
        }
        // A centered chain does not need a new proportion-based fit.
        if(previous.Skip(retainBase?1:0).Take(retainBase?2:3).Average(p=>Vector3.Distance(p,Project(p).Point))<h*.00025f)return null;
        var fitted=new[]{0f,.5f,.78f,1f}.Select(f=>
        {
            float target=distances[^1]*f;int i=Array.FindIndex(distances,d=>d>=target);if(i<=0)return path[0];
            return Vector3.Lerp(path[i-1],path[i],(target-distances[i-1])/Math.Max(distances[i]-distances[i-1],1e-8f));
        }).ToArray();
        if(retainBase)fitted[0]=previous[0];
        if(Vector3.Distance(previous[0],tip)>=Vector3.Distance(fitted[0],tip)*.6f)
        {
            var second=Project(previous[1]);var third=Project(previous[2]);
            if(second.Offset<h*.001f||third.Offset-second.Offset<h*.001f||distances[^1]-third.Offset<h*.001f)return null;
            fitted[1]=second.Point;fitted[2]=third.Point;
        }
        for(int bone=0;bone<3;bone++)for(int sample=0;sample<9;sample++)
            if(!volume.Contains(Vector3.Lerp(fitted[bone],fitted[bone+1],sample/9f),h*.00001f))return null;
        return fitted;
    }
    static ImportedCharacter HandSurface(ImportedCharacter character,Vector3 wrist,float radius)
    {
        var parts=new List<MeshPart>();float squared=radius*radius;
        foreach(var mesh in character.Meshes.Where(m=>m.Kind==MeshKind.Body))
        {
            var triangles=new List<int>();
            for(int i=0;i<mesh.Triangles.Length;i+=3)
            {
                var a=mesh.Vertices[mesh.Triangles[i]];var b=mesh.Vertices[mesh.Triangles[i+1]];var c=mesh.Vertices[mesh.Triangles[i+2]];
                var nearest=Vector3.Clamp(wrist,Vector3.Min(a,Vector3.Min(b,c)),Vector3.Max(a,Vector3.Max(b,c)));
                if(Vector3.DistanceSquared(nearest,wrist)>squared)continue;
                triangles.Add(mesh.Triangles[i]);triangles.Add(mesh.Triangles[i+1]);triangles.Add(mesh.Triangles[i+2]);
            }
            if(triangles.Count>0)parts.Add(new(mesh.Name,mesh.Vertices,triangles.ToArray(),MeshKind.Body));
        }
        return new ImportedCharacter{Meshes=parts.ToArray()};
    }
}
