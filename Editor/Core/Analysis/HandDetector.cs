#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Hand-local graph segmentation, competing palm cuts, branch extrema and centerline fitting.</summary>
public static class HandDetector
{
    public static void Detect(ImportedCharacter character,Anatomy anatomy,string side,Action<string>? diagnostic=null,int? expectedCount=null)
    {
        if(side is not ("L" or "R"))throw new ArgumentException("Unknown hand side.");
        if(expectedCount is <0 or >5)throw new ArgumentOutOfRangeException(nameof(expectedCount));
        var corrected=anatomy.Points.Values.Where(p=>p.Corrected&&p.Role.EndsWith("."+side)&&Profiles.Fingers.Any(p.Role.StartsWith)).ToArray();
        try
        {
            var forearm=anatomy["Hand."+side]-anatomy["LowerArm."+side];
            // A lowered hand's spherical neighborhood overlaps the hip/torso.
            // Follow the wrist's surface connectivity before looking for digits.
            bool lowered=forearm.LengthSquared()>0&&Vector3.Normalize(forearm).Y<-.82f;
            DetectGeometry(character,anatomy,side,diagnostic,lowered);
            int found=Profiles.Fingers.Count(f=>anatomy.Points.ContainsKey(f+"Tip."+side));
            if(expectedCount is {} requested?found<requested:found is >0 and <5)
            {
                var candidate=anatomy.Copy();DetectGeometry(character,candidate,side,diagnostic,true);
                int recovered=Profiles.Fingers.Count(f=>candidate.Points.ContainsKey(f+"Tip."+side));
                if(recovered>found&&(expectedCount is not null||PreservesExtrema(character,anatomy,candidate,side)))
                {
                    foreach(var key in anatomy.Points.Keys.Where(k=>k.EndsWith("."+side)&&Profiles.Fingers.Any(k.StartsWith)).ToArray())anatomy.Points.Remove(key);
                    foreach(var p in candidate.Points.Values.Where(p=>p.Role.EndsWith("."+side)&&Profiles.Fingers.Any(p.Role.StartsWith)))anatomy.Points[p.Role]=p;
                    if(candidate.Hands.TryGetValue(side,out var frame))anatomy.Hands[side]=frame;
                    anatomy.Warnings.RemoveAll(w=>w.StartsWith(side+" hand")||w.StartsWith(side+" fingers"));
                    anatomy.Warnings.AddRange(candidate.Warnings.Where(w=>w.StartsWith(side+" hand")||w.StartsWith(side+" fingers")));
                }
            }
            if(expectedCount is {} count)HandCountFitting.Apply(character,anatomy,side,count);
        }
        finally{foreach(var point in corrected)if(expectedCount is null||anatomy.Points.ContainsKey(point.Role))anatomy.Points[point.Role]=point;}
        ThumbFitting.Refine(character,anatomy,side);
        FingerAlignment.Refine(character,anatomy,side);
    }
    static bool PreservesExtrema(ImportedCharacter character,Anatomy original,Anatomy candidate,string side)
    {
        // A connected-surface candidate must retain the established extrema, while
        // exposing additional distinct branches. Finger names may change when an
        // isolated thumb is finally seen together with its neighboring fingers.
        var previous=original.Points.Values.Where(p=>p.Role.EndsWith("Tip."+side)).ToArray();
        var tips=candidate.Points.Values.Where(p=>p.Role.EndsWith("Tip."+side)).ToArray();
        float tolerance=original.Height*.012f;
        if(previous.Any(p=>tips.All(t=>Vector3.Distance(p.Position,t.Position)>tolerance)))return false;
        if(tips.SelectMany((p,i)=>tips.Skip(i+1).Select(q=>Vector3.Distance(p.Position,q.Position))).Any(d=>d<original.Height*.006f))return false;
        var surface=character.Meshes.Where(m=>m.Kind==MeshKind.Body).SelectMany(m=>m.Vertices).ToArray();
        if(tips.Any(t=>!Geometry.Finite(t.Position)))return false;
        var sparse=tips.Where(t=>!surface.Any(p=>Vector3.Distance(p,t.Position)<original.Height*.008f)).ToArray();
        if(sparse.Length==0)return true;
        // Sparse polygons can support a tip even when their vertices are far
        // apart. Check the actual triangles before rejecting that candidate.
        var triangles=new SurfaceVisibility(character.Meshes.Where(m=>m.Kind==MeshKind.Body));
        return sparse.All(t=>triangles.NearSurface(t.Position,original.Height*.008f));
    }
    static void DetectGeometry(ImportedCharacter character,Anatomy anatomy,string side,Action<string>? diagnostic,bool isolate=false)
    {
        anatomy.Hands.Remove(side);
        anatomy.HandEnds.Remove(side);
        foreach(var key in anatomy.Points.Keys.Where(k=>k.EndsWith("."+side)&&Profiles.Fingers.Any(k.StartsWith)).ToArray())anatomy.Points.Remove(key);
        anatomy.Warnings.RemoveAll(w=>w.StartsWith(side+" hand")||w.StartsWith(side+" fingers"));
        var reviewedWrist=anatomy.PalmCenters.GetValueOrDefault(side,anatomy["Hand."+side]);var axis=Vector3.Normalize(reviewedWrist-anatomy["LowerArm."+side]);float height=anatomy.Height;
        // Include the proximal thumb even when the body silhouette put the wrist too far distally.
        var wrist=reviewedWrist-axis*height*.045f;
        var across=Vector3.Cross(axis,Vector3.UnitZ);across=across.LengthSquared()>1e-8f?Vector3.Normalize(across):Vector3.UnitY;
        var surface=Geometry.Merge(character.Meshes.Where(m=>m.Kind==MeshKind.Body));
        // Close small gaps between separate joint caps and finger segments.
        // The radius is smaller than typical interdigital spacing.
        var neighbors=Geometry.Neighbors(surface,height*.001f);
        var connected=ReachableHand(surface,neighbors,reviewedWrist,height);
        var connectedPoints=surface.Vertices.Where((p,i)=>connected[i]).ToHashSet();
        var region=isolate?connected:Enumerable.Repeat(true,surface.Vertices.Length).ToArray();
        float cropRadius=height*.18f;
        var detached=HandRegions.Detached(surface,neighbors,reviewedWrist,axis,height,side);
        if(detached is not null)
        {
            var palm=surface.Vertices.Where((p,i)=>detached[i]).ToArray();
            float radius=palm.Max(p=>Vector3.Distance(p,wrist));
            if(radius>cropRadius)
            {
                region=detached;cropRadius=radius+height*.005f;
                anatomy.HandEnds[side]=Geometry.Mean(palm.OrderByDescending(p=>Vector3.DistanceSquared(p,reviewedWrist)).Take(Math.Max(4,palm.Length/5)));
            }
        }
        var parts=new[]{(Mesh:surface,Neighbors:neighbors)};
        var vertices=surface.Vertices.Where((p,i)=>region[i]&&Vector3.Dot(p-wrist,axis)>-height*.025f&&Vector3.Distance(p,wrist)<cropRadius).ToArray();
        if(vertices.Length<12){anatomy.Warnings.Add($"{side} hand has insufficient geometry for finger reconstruction.");return;}
        // Estimate palm spread in the plane perpendicular to the forearm. A
        // fixed world axis confuses palm-down hands with edge-on hands.
        var perpendicular=Vector3.Normalize(Vector3.Cross(axis,across));
        var palmMean=Geometry.Mean(vertices);float aa=0,bb=0,ab=0;
        foreach(var p in vertices)
        {
            var d=p-palmMean;float x=Vector3.Dot(d,across),y=Vector3.Dot(d,perpendicular);
            aa+=x*x;bb+=y*y;ab+=x*y;
        }
        float angle=.5f*MathF.Atan2(2*ab,aa-bb);
        across=Vector3.Normalize(across*MathF.Cos(angle)+perpendicular*MathF.Sin(angle));
        anatomy.Hands[side]=new(axis,Vector3.Normalize(Vector3.Cross(axis,across)),.4f);
        float reach=BodyDetector.Quantile(vertices.Select(p=>Vector3.Dot(p-wrist,axis)),.99f);
        diagnostic?.Invoke($"{side}: wrist {wrist}, axis {axis}, across {across}, reach {reach}");
        List<List<Vector3>> best=[];float bestScore=float.NegativeInfinity;
        var pooled=new List<List<Vector3>>();
        foreach(float fraction in isolate?Enumerable.Range(6,33).Reverse().Select(i=>i*.025f):new[]{.9f,.85f,.8f,.7f,.6f,.5f,.4f,.3f,.2f,.15f})
        {
            var branches=new List<List<Vector3>>();
            foreach(var part in parts)
            {
                var seen=new bool[part.Mesh.Vertices.Length];
                bool Allowed(int i){var p=part.Mesh.Vertices[i];return region[i]&&Vector3.Dot(p-wrist,axis)>reach*fraction&&Vector3.Distance(p,wrist)<cropRadius;}
                for(int i=0;i<seen.Length;i++)
                {
                    if(seen[i]||!Allowed(i))continue;
                    var queue=new Queue<int>();var points=new List<Vector3>();queue.Enqueue(i);seen[i]=true;
                    while(queue.TryDequeue(out var current))
                    {
                        points.Add(part.Mesh.Vertices[current]);
                        foreach(var n in part.Neighbors[current])if(!seen[n]&&Allowed(n)){seen[n]=true;queue.Enqueue(n);}
                    }
                    if(points.Count<8)continue;
                    var branchCenter=Geometry.Mean(points);
                    float wristDistance=Vector3.Distance(branchCenter,reviewedWrist);
                    bool leg= new[]{"L","R"}.Any(s=>Vector3.Distance(branchCenter,Geometry.ClosestOnSegment(branchCenter,anatomy["LowerLeg."+s],anatomy["Foot."+s]))<wristDistance*.65f);
                    if(leg&&!points.Any(connectedPoints.Contains))continue;
                    float width=points.Max(p=>Vector3.Dot(p-wrist,across))-points.Min(p=>Vector3.Dot(p-wrist,across));
                    float length=points.Max(p=>Vector3.Dot(p-wrist,axis))-points.Min(p=>Vector3.Dot(p-wrist,axis));
                    diagnostic?.Invoke($"cut {fraction}: count {points.Count}, width {width}, length {length}, tip {Tip(points,wrist,axis)}");
                    if(width<height*.035f&&length>height*.008f&&length>width*(isolate?.35f:.55f))branches.Add(points);
                }
            }
            foreach(var branch in branches)
            {
                // Nested cuts share actual surface vertices. Projected overlap can
                // merge separate fingers when the palm is rolled or fingers curl.
                var tip=Tip(branch,wrist,axis);var membership=branch.ToHashSet();
                float low=branch.Min(p=>Vector3.Dot(p-wrist,across)),high=branch.Max(p=>Vector3.Dot(p-wrist,across));
                var matches=pooled.Where(b=>
                {
                    if(isolate)return b.Any(membership.Contains);
                    var other=Tip(b,wrist,axis);float lateral=Vector3.Dot(other-wrist,across);
                    return lateral>=low-height*.002f&&lateral<=high+height*.002f&&Math.Abs(Vector3.Dot(other-tip,axis))<height*.02f;
                }).ToArray();
                // Follow a distal branch toward the palm only until it joins a
                // second finger. A fused proximal region is not a sixth digit.
                if(matches.Length>1)continue;
                var existing=matches.FirstOrDefault();
                if(existing is null)pooled.Add(branch);
                else if(branch.Count>existing.Count){pooled.Remove(existing);pooled.Add(branch);}
            }
            if(branches.Count is <1 or >5)continue;
            var tips=branches.Select(b=>Tip(b,wrist,axis)).ToArray();
            if(tips.SelectMany((p,i)=>tips.Skip(i+1).Select(q=>Vector3.Distance(p,q))).Any(d=>d<height*.009f))continue;
            float score=branches.Count+branches.Sum(b=>b.Max(p=>Vector3.Dot(p-wrist,axis))-b.Min(p=>Vector3.Dot(p-wrist,axis)))/height;
            if(score>bestScore){bestScore=score;best=branches;}
        }
        if(pooled.Count is >=1 and <=5 && pooled.Count>best.Count)best=pooled;
        if(best.Count==0){anatomy.Warnings.Add($"{side} fingers are not separated; palm-only skinning will be used.");return;}
        if(best.Count==1)
        {
            var branch=best[0];var tip=Tip(branch,wrist,axis);
            var start=Geometry.Mean(branch.OrderBy(p=>Vector3.Dot(p-wrist,axis)).Take(Math.Max(4,branch.Count/8)));
            // One isolated digit has no sibling ordering. Use the primary digit role rather than inventing a thumb and fingers.
            PlaceChain("Index",side,start,tip,vertices,height,anatomy);
            anatomy.Warnings.Add($"{side} hand has one detected finger branch; check its assignment.");return;
        }
        var ordered=best.OrderBy(b=>Vector3.Dot(Geometry.Mean(b)-wrist,across)).ToArray();
        // The body detector's wrist can be biased toward the thumb web. Use
        // the digit cluster's robust center to measure transverse separation.
        var spread=ordered.Select(b=>Vector3.Dot(Tip(b,wrist,axis)-wrist,across)).Order().ToArray();
        // Average the middle pair for even counts. Choosing one endpoint as the
        // center makes a two-digit hand change semantic roles when mirrored.
        float digitCenter=(spread[(spread.Length-1)/2]+spread[spread.Length/2])*.5f;
        int thumb=Enumerable.Range(0,ordered.Length).MaxBy(i=>
        {
            var tip=Tip(ordered[i],wrist,axis);return Math.Abs(Vector3.Dot(tip-wrist,across)-digitCenter)/Math.Max(Vector3.Dot(tip-wrist,axis),height*.01f);
        });
        var others=Enumerable.Range(0,ordered.Length).Where(i=>i!=thumb).OrderBy(i=>Math.Abs(i-thumb)).ToArray();
        var thumbDirection=Tip(ordered[thumb],wrist,axis)-Geometry.Mean(others.Select(i=>Tip(ordered[i],wrist,axis)));
        var thumbSpread=Vector3.Dot(thumbDirection,across)>=0?across:-across;
        anatomy.Hands[side]=new(axis,Vector3.Normalize(Vector3.Cross(axis,thumbSpread))*(side=="L"?1:-1),.6f);
        var names=new Dictionary<int,string>{{thumb,"Thumb"}};for(int i=0;i<others.Length;i++)names[others[i]]=Profiles.Fingers[i+1];
        float knuckles=BodyDetector.Quantile(others.Select(i=>ordered[i].Min(p=>Vector3.Dot(p-wrist,axis))),.2f);
        for(int i=0;i<ordered.Length;i++)
        {
            var branch=ordered[i];var tip=Tip(branch,wrist,axis);
            var basePoint=Geometry.Mean(branch.OrderBy(p=>Vector3.Dot(p-wrist,axis)).Take(Math.Max(4,branch.Count/8)));
            var direction=Vector3.Normalize(tip-basePoint);
            if(i==thumb)diagnostic?.Invoke($"thumb base: branch {basePoint}, reviewed wrist {reviewedWrist}, boundary {wrist}, tip {tip}");
            if(i==thumb)
            {
                // A detached digit has a real proximal cap. Preserve that evidence
                // instead of extending its root into the forearm. For a connected
                // thumb, retain the existing palm prior unless local graph isolation
                // was needed to separate the hand from neighboring body geometry.
                var cap=DetachedDigitBase(surface,neighbors,branch[0],reviewedWrist,direction,height);
                if(cap is {} proximal)basePoint=proximal;
                else if(isolate)basePoint-=direction*Math.Min(Vector3.Distance(basePoint,tip)*.2f,height*.02f);
                else basePoint=tip+direction*Vector3.Dot(wrist-tip,direction);
            }
            else basePoint+=axis*(knuckles-Vector3.Dot(basePoint-wrist,axis));
            PlaceChain(names[i],side,basePoint,tip,vertices,height,anatomy);
        }
        if(best.Count<5)anatomy.Warnings.Add($"{side} hand has {best.Count} detected finger branches; check their assignments.");
    }
    internal static bool[] ReachableHand(MeshPart mesh,List<int>[] neighbors,Vector3 wrist,float height)
    {
        var distance=Enumerable.Repeat(float.PositiveInfinity,mesh.Vertices.Length).ToArray();var queue=new PriorityQueue<int,float>();
        float nearest=mesh.Vertices.Min(p=>Vector3.Distance(p,wrist));
        for(int v=0;v<mesh.Vertices.Length;v++)
        {
            float d=Vector3.Distance(mesh.Vertices[v],wrist);
            if(d>nearest+height*.012f)continue;
            distance[v]=d;queue.Enqueue(v,d);
        }
        while(queue.TryDequeue(out int vertex,out float cost))
        {
            if(cost>distance[vertex])continue;
            foreach(int next in neighbors[vertex])
            {
                float candidate=cost+Vector3.Distance(mesh.Vertices[vertex],mesh.Vertices[next]);
                if(candidate>=distance[next]||candidate>height*.22f)continue;
                distance[next]=candidate;queue.Enqueue(next,candidate);
            }
        }
        return distance.Select(float.IsFinite).ToArray();
    }
    static Vector3? DetachedDigitBase(MeshPart mesh,List<int>[] neighbors,Vector3 seed,Vector3 wrist,Vector3 direction,float height)
    {
        int start=Array.IndexOf(mesh.Vertices,seed);if(start<0)return null;
        var visited=new bool[mesh.Vertices.Length];var queue=new Queue<int>();var points=new List<Vector3>();
        queue.Enqueue(start);visited[start]=true;
        while(queue.TryDequeue(out int v))
        {
            var point=mesh.Vertices[v];if(Vector3.Distance(point,wrist)>height*.14f)return null;
            points.Add(point);
            foreach(int next in neighbors[v])if(!visited[next]){visited[next]=true;queue.Enqueue(next);}
        }
        // A whole detached hand is not a detached digit. Its transverse spread
        // must fit a finger envelope before treating the component's cap as a joint.
        var center=Geometry.Mean(points);float radius=points.Max(p=>((p-center)-direction*Vector3.Dot(p-center,direction)).Length());
        if(radius>height*.018f)return null;
        return Geometry.Mean(points.OrderBy(p=>Vector3.Dot(p,direction)).Take(Math.Max(4,points.Count/15)));
    }
    static void PlaceChain(string name,string side,Vector3 start,Vector3 tip,Vector3[] vertices,float height,Anatomy anatomy)
    {
        for(int joint=1;joint<=3;joint++)
        {
            var seed=Vector3.Lerp(start,tip,(joint-1)/3f);
            anatomy.Set(name+joint+"."+side,BodyDetector.RefineCenter(vertices,seed,tip-start,height*.006f),.60f);
        }
        anatomy.Set(name+"Tip."+side,tip,.70f);
    }
    static Vector3 Tip(List<Vector3> branch,Vector3 wrist,Vector3 axis)=>Geometry.Mean(branch.OrderByDescending(p=>Vector3.Dot(p-wrist,axis)).Take(Math.Max(4,branch.Count/10)));
}
