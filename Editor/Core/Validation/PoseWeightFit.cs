#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;
using Quaternion=System.Numerics.Quaternion;

/// <summary>Fit local skin weights against signed surface volume in every stress pose.
/// Trials stay private until PoseWeightRepair validates the complete candidate.</summary>
internal static class PoseWeightFit
{
    sealed record Vertex(WeightSeams.Vertex[] Aliases,Vector3 Point,int[] Bones,double[] Initial,double[] Caps,bool Editable);
    sealed record Face(int A,int B,int C,Vector3 Normal,float Area,float AB,float BC,float CA);
    sealed record Sample(Vector3[] Bones,Quaternion[] Rotations,Vector3[][] Delta);
    sealed record Correction(double[][] S,double[][] Y,double Rho,double Scale);
    const double VolumeMargin=.05,StretchMargin=3.8;

    internal static Influence[][][] Solve(ImportedCharacter character,GeneratedRig rig,ValidationGeometry geometry,ValidationReport initial,WeightSeams seams,bool expandSupport,bool repairSupport,int iterations,bool prioritize=false)
    {

        // Keep the ordinary fit's safety margins until sparse-support repair is
        // necessary. In that fallback, actual volume/area violations take priority.
        double violationPenalty=prioritize?1000:0;
        var roles=rig.Bones.Select(b=>b.Role).ToHashSet();
        var specs=geometry.Poses.Where(p=>Deformation.IsApplicable(p,roles)).ToArray();
        var transforms=specs.Select(p=>geometry.Transforms(rig,p)).ToArray();
        var seeds=character.Meshes.Select(_=>new HashSet<int>()).ToArray();
        var support=character.Meshes.Select(_=>new Dictionary<int,Dictionary<int,float>>()).ToArray();
        var buffer=character.Meshes.Select(m=>new Vector3[m.Vertices.Length]).ToArray();
        for(int sample=0;sample<specs.Length;sample++)
        {
            var report=initial.StressTests[sample];
            if(report.ReversedTriangles==0&&report.MaximumStretch<=4&&report.MinimumAreaRatio>=.025)continue;
            var transform=transforms[sample];
            Deformation.ApplyTransforms(character,rig,transform.Positions,transform.Rotations,buffer);
            for(int p=0;p<character.Meshes.Length;p++)foreach(var f in geometry.Faces[p])
            {
                if(f.Area<=geometry.Height*geometry.Height*1e-10)continue;
                var normal=Vector3.Cross(buffer[p][f.B]-buffer[p][f.A],buffer[p][f.C]-buffer[p][f.A]);
                bool bad=SurfaceOrientation.Alignment(f.Normal,normal,rig.Weights[p][f.A],rig.Weights[p][f.B],rig.Weights[p][f.C],transform.Rotations)<SurfaceOrientation.ReversalLimit||normal.Length()/f.Area<.025;
                for(int edge=0;edge<3;edge++){var(a,b,length)=f.Edge(edge);if(length>geometry.Height*1e-6&&Vector3.Distance(buffer[p][a],buffer[p][b])/length>4)bad=true;}
                if(bad)
                {
                    foreach(int v in new[]{f.A,f.B,f.C})
                    {
                        seeds[p].Add(v);var point=character.Meshes[p].Vertices[v];var row=rig.Weights[p][v];
                        Vector3 Posed(int b)=>transform.Positions[b]+Vector3.Transform(point-rig.Bones[b].Position,transform.Rotations[b]);
                        var first=Posed(row[0].Bone);
                        if(row.Any(w=>Vector3.Distance(Posed(w.Bone),first)>geometry.Height*1e-5f))continue;
                        var alternative=geometry.Neighbors[p][v].SelectMany(n=>rig.Weights[p][n])
                            .Where(w=>!row.Any(old=>old.Bone==w.Bone)&&Vector3.Distance(Posed(w.Bone),first)>geometry.Height*1e-5f)
                            .GroupBy(w=>w.Bone).OrderByDescending(g=>g.Sum(w=>w.Weight)).ThenBy(g=>g.Key).FirstOrDefault();
                        if(alternative is null)continue;
                        if(!support[p].TryGetValue(v,out var desired))support[p][v]=desired=[];
                        desired[alternative.Key]=desired.GetValueOrDefault(alternative.Key)+alternative.Sum(w=>w.Weight);
                    }
                }
            }
        }
        if(seeds.All(s=>s.Count==0))return rig.Weights;
        var editable=seeds.Select(s=>s.ToHashSet()).ToArray();
        void CloseSeams()
        {
            var groups=editable.SelectMany((s,p)=>s.Select(v=>seams.Groups[p][v])).Distinct().ToArray();
            foreach(int group in groups)foreach(var v in seams.Vertices[group])editable[v.Part].Add(v.Index);
        }
        CloseSeams();
        for(int ring=0;ring<2;ring++)
        {
            editable=editable.Select((s,p)=>s.Concat(s.SelectMany(v=>geometry.Neighbors[p][v])).ToHashSet()).ToArray();
            CloseSeams();
        }
        if(repairSupport)
        {
            float radius=geometry.Height*.03f;
            var distance=character.Meshes.Select(m=>Enumerable.Repeat(float.PositiveInfinity,m.Vertices.Length).ToArray()).ToArray();
            var queue=new PriorityQueue<WeightSeams.Vertex,float>();
            void Visit(int p,int v,float value)
            {
                if(value>radius||value>=distance[p][v])return;
                distance[p][v]=value;queue.Enqueue(new(p,v),value);
            }
            for(int p=0;p<seeds.Length;p++)foreach(int v in seeds[p])Visit(p,v,0);
            while(queue.TryDequeue(out var v,out float value))
            {
                if(value>distance[v.Part][v.Index])continue;editable[v.Part].Add(v.Index);
                foreach(var alias in seams.Vertices[seams.Groups[v.Part][v.Index]])Visit(alias.Part,alias.Index,value);
                var point=character.Meshes[v.Part].Vertices[v.Index];
                foreach(int n in geometry.Neighbors[v.Part][v.Index])Visit(v.Part,n,value+Vector3.Distance(point,character.Meshes[v.Part].Vertices[n]));
            }
        }
        var selectedFaces=geometry.Faces.Select((part,p)=>part.Where(f=>editable[p].Contains(f.A)||editable[p].Contains(f.B)||editable[p].Contains(f.C)).ToArray()).ToArray();
        var indices=character.Meshes.Select(m=>Enumerable.Repeat(-1,m.Vertices.Length).ToArray()).ToArray();
        var vertices=new List<Vertex>();var faces=new List<Face>();
        var groupIndices=Enumerable.Repeat(-1,seams.Vertices.Length).ToArray();
        var ends=RigGeometry.SegmentEnds(rig);var locality=new SkinningLocality(character,rig,ends);
        for(int p=0;p<selectedFaces.Length;p++)
        {
            foreach(int v in selectedFaces[p].SelectMany(f=>new[]{f.A,f.B,f.C}).Distinct().Order())
            {
                int group=seams.Groups[p][v];
                if(groupIndices[group]>=0){indices[p][v]=groupIndices[group];continue;}
                var aliases=seams.Vertices[group];
                var weights=rig.Weights[p][v];var point=character.Meshes[p].Vertices[v];
                var desired=aliases.Where(a=>support[a.Part].ContainsKey(a.Index)).SelectMany(a=>support[a.Part][a.Index])
                    .GroupBy(x=>x.Key).OrderByDescending(g=>g.Sum(x=>x.Value)).ThenBy(g=>g.Key).FirstOrDefault();
                if(repairSupport&&desired is not null)
                {
                    int extra=desired.Key;
                    var proposed=weights.OrderByDescending(w=>w.Weight).Take(rig.Profile.MaximumInfluences-1).Append(new Influence(extra,.01f));
                    var replacement=Skinning.Cleanup(proposed,rig.Bones.Length,rig.Profile.MaximumInfluences);
                    if(Vector3.Distance(point,Geometry.ClosestOnSegment(point,rig.Bones[extra].Position,ends[extra]))<=locality.Limit(extra,point)&&aliases.All(a=>geometry.Allows(a.Part,a.Index,point,replacement)))weights=replacement;
                }
                if(expandSupport&&editable[p].Contains(v))
                {
                    var retained=weights.Select(w=>w.Bone).ToHashSet();
                    var extra=aliases.SelectMany(a=>geometry.Neighbors[a.Part][a.Index].SelectMany(n=>rig.Weights[a.Part][n])).Where(w=>!retained.Contains(w.Bone))
                        .GroupBy(w=>w.Bone).OrderByDescending(g=>g.Sum(w=>w.Weight)).ThenBy(g=>g.Key)
                        .Where(g=>Vector3.Distance(point,Geometry.ClosestOnSegment(point,rig.Bones[g.Key].Position,ends[g.Key]))<=locality.Limit(g.Key,point)&&aliases.All(a=>geometry.InfluenceAllowed?.Invoke(a.Part,a.Index,g.Key)!=false))
                        .Take(4).Select(g=>new Influence(g.Key,0));
                    weights=weights.Concat(extra).ToArray();
                }
                var caps=weights.Select(w=>Vector3.Distance(point,Geometry.ClosestOnSegment(point,rig.Bones[w.Bone].Position,ends[w.Bone]))>locality.Limit(w.Bone,point)?Math.Max(w.Weight,.05):1d).ToArray();
                if(geometry.InfluenceAllowed is not null)for(int i=0;i<weights.Length;i++)
                    if(aliases.Any(a=>!geometry.InfluenceAllowed(a.Part,a.Index,weights[i].Bone)))caps[i]=0;
                if(geometry.Trunk is {} trunk&&aliases.Any(a=>trunk.Vertices[a.Part][a.Index]))for(int i=0;i<weights.Length;i++)
                    if(trunk.Attachments.Any(a=>a.Support(point)==0&&a.Moving[weights[i].Bone]))caps[i]=weights[i].Weight;
                indices[p][v]=vertices.Count;groupIndices[group]=vertices.Count;
                vertices.Add(new(aliases,point,weights.Select(w=>w.Bone).ToArray(),weights.Select(w=>(double)w.Weight).ToArray(),caps,editable[p].Contains(v)&&weights.Length>1));
            }
            foreach(var f in selectedFaces[p])faces.Add(new(indices[p][f.A],indices[p][f.B],indices[p][f.C],f.Normal,f.Area,f.AB,f.BC,f.CA));
        }
        var activeBones=vertices.SelectMany(v=>v.Bones).Distinct().ToArray();
        var sampleList=new List<Sample>();
        foreach(var t in transforms)
        {
            if(sampleList.Any(s=>activeBones.All(b=>s.Bones[b]==t.Positions[b]&&s.Rotations[b]==t.Rotations[b])))continue;
            sampleList.Add(new(t.Positions,t.Rotations,vertices.Select(v=>v.Bones.Select(b=>t.Positions[b]+Vector3.Transform(v.Point-rig.Bones[b].Position,t.Rotations[b])-v.Point).ToArray()).ToArray()));
        }
        var samples=sampleList.ToArray();
        // A face normal carried by a bone does not depend on the weights being
        // fitted, yet every evaluation and gradient rotated it again for each
        // corner influence. Rotate once per pose; the same values then enter the
        // same arithmetic. Very large regions keep the direct path to bound memory.
        // Most samples turn a single joint. A face none of whose bones turn
        // rests at its bind shape, far inside every margin below, and adds
        // exactly nothing to the loss or its gradient. Visit only the others.
        var turned=samples.Select(sample=>
        {
            var moved=vertices.Select(v=>v.Bones.Any(b=>sample.Rotations[b]!=Quaternion.Identity||Vector3.Distance(sample.Bones[b],rig.Bones[b].Position)>geometry.Height*1e-6f)).ToArray();
            var active=Enumerable.Range(0,faces.Count).Where(f=>moved[faces[f].A]||moved[faces[f].B]||moved[faces[f].C]).ToArray();
            var used=active.SelectMany(f=>new[]{faces[f].A,faces[f].B,faces[f].C}).Distinct().Order().ToArray();
            return(Faces:active,Vertices:used);
        }).ToArray();
        var corners=new int[faces.Count*3+1];
        for(int f=0;f<faces.Count;f++)
        {
            corners[f*3+1]=corners[f*3]+vertices[faces[f].A].Bones.Length;
            corners[f*3+2]=corners[f*3+1]+vertices[faces[f].B].Bones.Length;
            corners[f*3+3]=corners[f*3+2]+vertices[faces[f].C].Bones.Length;
        }
        Vector3[][]? carried=(long)corners[^1]*samples.Length>8_000_000?null:samples.Select(sample=>
        {
            var normals=new Vector3[corners[^1]];
            for(int f=0;f<faces.Count;f++)
            {
                var face=faces[f];int at=corners[f*3];
                foreach(int v in new[]{face.A,face.B,face.C})foreach(int bone in vertices[v].Bones)normals[at++]=Vector3.Transform(face.Normal,sample.Rotations[bone]);
            }
            return normals;
        }).ToArray();
        var weightsNow=vertices.Select(v=>v.Initial.ToArray()).ToArray();var candidate=vertices.Select(v=>new double[v.Bones.Length]).ToArray();
        var gradient=vertices.Select(v=>new double[v.Bones.Length]).ToArray();var previousGradient=vertices.Select(v=>new double[v.Bones.Length]).ToArray();
        var previous=vertices.Select(v=>new double[v.Bones.Length]).ToArray();
        var points=new Vector3[vertices.Count];
        double step=0.001;var history=new List<Correction>();
        for(int iteration=0;iteration<iterations;iteration++)
        {
            double loss=Evaluate(weightsNow,gradient);
            double max=0,ss=0,sy=0;
            for(int v=0;v<vertices.Count;v++)
            {
                double mean=gradient[v].Average();
                for(int i=0;i<gradient[v].Length;i++)
                {
                    gradient[v][i]=vertices[v].Editable?gradient[v][i]-mean:0;
                    double s=weightsNow[v][i]-previous[v][i],y=gradient[v][i]-previousGradient[v][i];ss+=s*s;sy+=s*y;
                    max=Math.Max(max,Math.Abs(gradient[v][i]));
                }
            }
            if(loss<1e-9||max<1e-10)break;
            if(iteration>0&&sy>1e-12&&ss>1e-12)
            {
                var s=weightsNow.Select((w,v)=>w.Select((x,i)=>x-previous[v][i]).ToArray()).ToArray();
                var y=gradient.Select((g,v)=>g.Select((x,i)=>x-previousGradient[v][i]).ToArray()).ToArray();
                history.Add(new(s,y,1/sy,sy/Math.Max(Dot(y,y),1e-12)));if(history.Count>8)history.RemoveAt(0);
            }
            var direction=gradient.Select(g=>g.ToArray()).ToArray();
            var alpha=new double[history.Count];
            for(int h=history.Count-1;h>=0;h--){alpha[h]=history[h].Rho*Dot(history[h].S,direction);Add(direction,history[h].Y,-alpha[h]);}
            double scale=history.Count>0?history[^1].Scale:.01/Math.Max(max,1e-6);
            foreach(var row in direction)for(int i=0;i<row.Length;i++)row[i]*=scale;
            for(int h=0;h<history.Count;h++){double beta=history[h].Rho*Dot(history[h].Y,direction);Add(direction,history[h].S,alpha[h]-beta);}
            step=1;
            bool improved=false;
            for(int search=0;search<14;search++,step*=.5)
            {
                for(int v=0;v<vertices.Count;v++)
                {
                    for(int i=0;i<candidate[v].Length;i++)candidate[v][i]=weightsNow[v][i]-step*direction[v][i];
                    if(vertices[v].Editable)Project(candidate[v],vertices[v].Caps);
                }
                double next=Evaluate(candidate,null);
                if(next<loss-1e-12)
                {
                    for(int v=0;v<vertices.Count;v++)
                    {
                        weightsNow[v].CopyTo(previous[v],0);gradient[v].CopyTo(previousGradient[v],0);candidate[v].CopyTo(weightsNow[v],0);
                    }
                    improved=true;break;
                }
            }
            if(!improved)break;
        }
        var result=rig.Weights.Select(p=>(Influence[][])p.Clone()).ToArray();
        for(int v=0;v<vertices.Count;v++)if(vertices[v].Editable)
        {
            var item=vertices[v];
            var row=item.Bones.Select((b,i)=>new Influence(b,(float)weightsNow[v][i])).Where(w=>w.Weight>1e-8f).ToArray();
            var fitted=expandSupport?Skinning.Cleanup(row,rig.Bones.Length,rig.Profile.MaximumInfluences):row;
            foreach(var alias in item.Aliases)result[alias.Part][alias.Index]=fitted;
        }
        return result;

        double Evaluate(double[][] values,double[][]? g)
        {
            if(g is not null)foreach(var row in g)Array.Clear(row);
            double loss=0;
            for(int sampleIndex=0;sampleIndex<samples.Length;sampleIndex++)
            {
                var sample=samples[sampleIndex];var rotated=carried?[sampleIndex];
                foreach(int v in turned[sampleIndex].Vertices)
                {
                    var point=vertices[v].Point;for(int i=0;i<values[v].Length;i++)point+=sample.Delta[v][i]*(float)values[v][i];points[v]=point;
                }
                foreach(int faceIndex in turned[sampleIndex].Faces)
                {
                    var f=faces[faceIndex];
                    if(f.Area<=geometry.Height*geometry.Height*1e-10)continue;
                    var a=points[f.A];var b=points[f.B];var c=points[f.C];var n=Vector3.Cross(b-a,c-a);
                    var transported=Transport(f.A,0)+Transport(f.B,1)+Transport(f.C,2);
                    double denominator=f.Area*f.Area,volume=Vector3.Dot(n,transported)/denominator;
                    double deficit=VolumeMargin-volume;
                    double violation=Math.Max(0,.001-volume);
                    loss+=violationPenalty*.5*violation*violation;
                    if(deficit>0)
                    {
                        loss+=.5*deficit*deficit;
                        if(g is not null)
                        {
                            AddVolume(f.A,0,Vector3.Cross(b-c,transported));AddVolume(f.B,1,Vector3.Cross(c-a,transported));AddVolume(f.C,2,Vector3.Cross(a-b,transported));
                        }
                    }
                    Edge(f.A,f.B,f.AB);Edge(f.B,f.C,f.BC);Edge(f.C,f.A,f.CA);
                    Vector3 Carried(int v,int corner,int i)=>rotated is null?Vector3.Transform(f.Normal,sample.Rotations[vertices[v].Bones[i]]):rotated[corners[faceIndex*3+corner]+i];
                    Vector3 Transport(int v,int corner)
                    {
                        var normal=Vector3.Zero;
                        for(int i=0;i<values[v].Length;i++)normal+=Carried(v,corner,i)*((float)values[v][i]/3);
                        return normal;
                    }
                    void AddVolume(int v,int corner,Vector3 derivative)
                    {
                        if(!vertices[v].Editable)return;
                        for(int i=0;i<g![v].Length;i++)
                            g[v][i]-=(deficit+violationPenalty*violation)*(Vector3.Dot(derivative,sample.Delta[v][i])+Vector3.Dot(n,Carried(v,corner,i))/3)/denominator;
                    }
                    double areaDeficit=.03-n.Length()/f.Area;
                    if(areaDeficit>0)
                    {
                        loss+=violationPenalty*.5*areaDeficit*areaDeficit;
                        if(g is not null)
                        {
                            var direction=n/Math.Max(n.Length(),1e-12f);
                            AreaGradient(f.A,Vector3.Cross(b-c,direction));AreaGradient(f.B,Vector3.Cross(c-a,direction));AreaGradient(f.C,Vector3.Cross(a-b,direction));
                        }
                        void AreaGradient(int v,Vector3 derivative)
                        {
                            if(!vertices[v].Editable)return;
                            for(int i=0;i<g![v].Length;i++)g[v][i]-=violationPenalty*areaDeficit*Vector3.Dot(derivative,sample.Delta[v][i])/f.Area;
                        }
                    }
                    void Edge(int first,int second,float length)
                    {
                        if(length<=geometry.Height*1e-6)return;
                        var delta=points[first]-points[second];float posedLength=delta.Length();double excess=posedLength/length-StretchMargin;
                        if(excess<=0)return;loss+=.5*excess*excess;
                        if(g is null)return;
                        var direction=delta/(posedLength*length);
                        if(vertices[first].Editable)for(int i=0;i<g[first].Length;i++)g[first][i]+=excess*Vector3.Dot(direction,sample.Delta[first][i]);
                        if(vertices[second].Editable)for(int i=0;i<g[second].Length;i++)g[second][i]-=excess*Vector3.Dot(direction,sample.Delta[second][i]);
                    }
                }
            }
            return loss;
        }
    }
    static double Dot(double[][] a,double[][] b)
    {
        double result=0;for(int v=0;v<a.Length;v++)for(int i=0;i<a[v].Length;i++)result+=a[v][i]*b[v][i];return result;
    }
    static void Add(double[][] a,double[][] b,double scale)
    {
        for(int v=0;v<a.Length;v++)for(int i=0;i<a[v].Length;i++)a[v][i]+=scale*b[v][i];
    }
    static void Project(double[] values,double[] caps)
    {
        double low=values.Select((v,i)=>v-caps[i]).Min(),high=values.Max();
        for(int step=0;step<40;step++)
        {
            double lambda=(low+high)*.5,sum=0;for(int i=0;i<values.Length;i++)sum+=Math.Clamp(values[i]-lambda,0,caps[i]);
            if(sum>1)low=lambda;else high=lambda;
        }
        for(int i=0;i<values.Length;i++)values[i]=Math.Clamp(values[i]-(low+high)*.5,0,caps[i]);
    }
}
