#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Separate skull ownership at a measured neck contour. Nearby shoulders
/// must not pull the face; disconnected cranial surfaces retain their topology.</summary>
internal static class HeadWeightRepair
{
    internal static GeneratedRig Improve(ImportedCharacter character,GeneratedRig rig)
    {
        if(!rig.Report.Passed)return rig;
        int head=Array.FindIndex(rig.Bones,b=>b.Role=="Head"),neck=Array.FindIndex(rig.Bones,b=>b.Role=="Neck");
        if(head<0||neck<0)return rig;
        float height=character.AnatomicalHeight;var origin=rig.Bones[neck].Position;
        var section=NeckSection(character,origin,height);
        if(section is null)return rig;
        origin=section.Center;
        var skull=new bool[rig.Bones.Length];skull[head]=true;
        for(int b=head+1;b<skull.Length;b++)skull[b]=rig.Bones[b].Parent>=0&&skull[rig.Bones[b].Parent];
        var axial=new bool[rig.Bones.Length];for(int b=neck;b>=0;b=rig.Bones[b].Parent)axial[b]=true;
        float transitionHeight=Math.Max(section.Radius*.5f,height*.005f);
        var mesh=Geometry.Merge(character.Meshes);var graph=Geometry.Neighbors(mesh,height*1e-5f);
        for(int v=0;v<graph.Length;v++)graph[v].RemoveAll(n=>
        {
            float a=mesh.Vertices[v].Y-origin.Y,b=mesh.Vertices[n].Y-origin.Y;
            if(!((a<=0&&b>0)||(b<=0&&a>0)))return false;
            // Cut the analysis graph across outer hair shells too. Anatomy
            // classifies each resulting component before it receives weights.
            return true;
        });
        var components=Geometry.Components(graph);var ends=RigGeometry.SegmentEnds(rig);
        var cranial=new bool[components.Max()+1];var below=new bool[cranial.Length];
        var minimum=Enumerable.Repeat(new Vector3(float.PositiveInfinity),cranial.Length).ToArray();
        var maximum=Enumerable.Repeat(new Vector3(float.NegativeInfinity),cranial.Length).ToArray();
        bool NearHead(Vector3 point)
        {
            float distance=Vector3.DistanceSquared(point,Geometry.ClosestOnSegment(point,rig.Bones[head].Position,ends[head]));
            float other=Enumerable.Range(0,rig.Bones.Length).Where(b=>rig.Bones[b].Deform&&!skull[b]&&b!=neck)
                .Select(b=>Vector3.DistanceSquared(point,Geometry.ClosestOnSegment(point,rig.Bones[b].Position,ends[b]))).DefaultIfEmpty(float.PositiveInfinity).Min();
            return distance<other*.5f;
        }
        for(int v=0;v<mesh.Vertices.Length;v++)
        {
            var point=mesh.Vertices[v];int component=components[v];
            minimum[component]=Vector3.Min(minimum[component],point);maximum[component]=Vector3.Max(maximum[component],point);
            if(point.Y<origin.Y-height*.001f)below[component]=true;
            if(point.Y<=origin.Y)continue;
            if(NearHead(point))cranial[component]=true;
        }
        // Sparse skulls may have only distant corner vertices. Confirm their
        // bounds center against the volume without relying on vertex density.
        var volume=new Lazy<SurfaceVisibility>(()=>new(character.Meshes.Where(m=>m.Kind==MeshKind.Body),height*1e-5f));
        for(int c=0;c<cranial.Length;c++)if(!below[c]&&!cranial[c])
        {
            var center=(minimum[c]+maximum[c])*.5f;
            cranial[c]=NearHead(center)&&volume.Value.Contains(center,height*1e-5f);
        }
        var selected=character.Meshes.Select(m=>new bool[m.Vertices.Length]).ToArray();int offset=0;
        for(int p=0;p<selected.Length;offset+=selected[p++].Length)
            if(character.Meshes[p].Kind is MeshKind.Body or MeshKind.Clothing or MeshKind.Hair)
                for(int v=0;v<selected[p].Length;v++){int c=components[offset+v];selected[p][v]=cranial[c]&&!below[c];}
        bool Allows(int p,int v,int b)=>!selected[p][v]||skull[b]||axial[b]&&character.Meshes[p].Vertices[v].Y<origin.Y+transitionHeight;
        bool Bleeds(GeneratedRig candidate)
        {
            for(int p=0;p<selected.Length;p++)for(int v=0;v<selected[p].Length;v++)if(selected[p][v])
                foreach(var w in candidate.Weights[p][v])if(w.Weight>1e-6f&&!Allows(p,v,w.Bone))return true;
            return false;
        }
        if(!Bleeds(rig))return rig;
        var candidate=new GeneratedRig{Profile=rig.Profile,Bones=rig.Bones,Anatomy=rig.Anatomy,Weights=rig.Weights.Select(p=>(Influence[][])p.Clone()).ToArray()};
        for(int p=0;p<selected.Length;p++)for(int v=0;v<selected[p].Length;v++)if(selected[p][v])
        {
            var values=new float[rig.Bones.Length];float removed=0;
            float blend=TrunkRegion.Smooth((character.Meshes[p].Vertices[v].Y-origin.Y)/transitionHeight);
            foreach(var w in rig.Weights[p][v])
            {
                if(skull[w.Bone])values[w.Bone]+=w.Weight;
                else if(w.Bone==neck){values[neck]+=w.Weight*(1-blend);removed+=w.Weight*blend;}
                else removed+=w.Weight;
            }
            values[head]+=removed;candidate.Weights[p][v]=Skinning.Cleanup(values,rig.Profile.MaximumInfluences);
        }
        var geometry=new ValidationGeometry(character,trunk:new TrunkRegion(character,rig),influenceAllowed:Allows);
        candidate.Report=RigValidator.ValidateAndRepair(character,candidate,geometry);
        if(!candidate.Report.Passed||candidate.Report.StressTests.Any(p=>p.ReversedTriangles>0))
            candidate=PoseWeightRepair.Improve(character,candidate,geometry,JointCoverage.Measure(character,candidate,geometry));
        if(candidate.Report.Passed)candidate=SeamWeightRepair.Improve(character,JointCoverage.Improve(character,candidate,geometry),geometry);
        if(candidate.Report.Passed&&!Bleeds(candidate))
        {
            candidate.Report.Repairs+=rig.Report.Repairs;candidate.Report.RepairPasses+=rig.Report.RepairPasses+1;return candidate;
        }
        rig.Report.Issues.Add(new("head-weight-bleeding","The face still follows a shoulder or torso joint. Check the neck and head landmarks.",true));
        return rig;
    }
    static MeshSections.Section? NeckSection(ImportedCharacter character,Vector3 origin,float height)
    {
        MeshSections.Section? At(Vector3 point)
        {
            // Separate material parts can quantize the same seam differently.
            // Retry the contour with a small relative gap tolerance;
            // this joins analysis endpoints without welding source geometry.
            foreach(float tolerance in new[]{1e-5f,1e-4f})
            {
                var found=MeshSections.Cut(character,point,Vector3.UnitY,height*.15f,height*tolerance)
                    .Where(s=>s.Radius>height*.005f&&s.Radius<height*.08f&&Vector3.Distance(s.Center,point)<s.Radius*.5f)
                    .MinBy(s=>Vector3.DistanceSquared(s.Center,point));
                if(found is not null)return found;
            }
            return null;
        }
        var section=At(origin);if(section is not null)return section;
        // A coarse or block-shaped skull can contain the predicted neck joint.
        // Confirm a narrower neck shaft below it before assigning skull weights.
        var lower=Enumerable.Range(1,16).Select(i=>At(origin-Vector3.UnitY*(height*.005f*i))).OfType<MeshSections.Section>().ToArray();
        if(lower.Length<2)return null;
        float radius=lower.Min(s=>s.Radius);var shaft=lower.Where(s=>s.Radius<radius*1.1f).ToArray();
        if(shaft.Length<2)return null;
        float middle=shaft.Average(s=>s.Center.Y);
        return shaft.MinBy(s=>Math.Abs(s.Center.Y-middle));
    }
}
