namespace HumanoidRigger.EditorTools.Core.RigProfiles;
using Vector3=System.Numerics.Vector3;

public static class ReferenceSkin
{
    /// <summary>Actual skin clusters decide deform membership. Control names and
    /// proximity alone cannot tell an attachment from a deforming helper.</summary>
    public static HashSet<string> WeightedBones(byte[] fbx)
    {
        var root=FbxTokenizer.Parse(fbx);var scene=FbxScene.Build(root);
        var clusters=scene.ObjectsById.Values.Where(o=>o.SubClass=="Cluster"&&o.Node.Child("Weights")?.AsDoubleArray(0).Any(w=>w>0)==true).Select(o=>o.Id).ToHashSet();
        var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var c in root.Child("Connections")?.ChildrenNamed("C")??[])
            if(c.Properties.Count>=3&&c.Prop<string>(0)=="OO"&&clusters.Contains(c.Prop<long>(2))&&scene.ObjectsById.TryGetValue(c.Prop<long>(1),out var model)&&model.NodeType=="Model")result.Add(model.Name);
        if(result.Count==0)throw new FormatException("The reference contains no skin clusters.");
        return result;
    }

    /// <summary>The stock knee, ankle and elbow weight profiles: the share of each limb bone at each
    /// position along the limb, measured on the reference mesh. Joints whose bones are missing are skipped.</summary>
    public static JointProfile[] JointProfiles(byte[] fbx)
    {
        var root=FbxTokenizer.Parse(fbx);var objects=root.Child("Objects");if(objects is null)return [];
        var names=new Dictionary<long,string>();var geometry=new Dictionary<long,double[]>();
        var clusters=new Dictionary<long,(int[] Index,double[] Weight,Vector3 Link)>();
        foreach(var node in objects.Children)
        {
            if(node.Properties.Count<2)continue;
            long id=node.Prop<long>(0);names[id]=FbxNode.SplitName(node.AsString(1)).Name;
            if(node.Name=="Geometry"&&node.Child("Vertices") is {} vertices)geometry[id]=vertices.AsDoubleArray(0);
            if(node.Name=="Deformer"&&node.Properties.Count>2&&node.AsString(2)=="Cluster"&&node.Child("Indexes") is {} indexes&&node.Child("Weights") is {} weights&&node.Child("TransformLink") is {} link)
            {
                var m=link.AsDoubleArray(0);
                clusters[id]=(indexes.AsIntArray(0),weights.AsDoubleArray(0),new Vector3((float)m[12],(float)m[13],(float)m[14]));
            }
        }
        var bone=new Dictionary<long,string>();var skinOf=new Dictionary<long,long>();var geometryOf=new Dictionary<long,long>();
        foreach(var c in root.Child("Connections")?.ChildrenNamed("C")??[])
        {
            if(c.Properties.Count<3)continue;
            long child=c.Prop<long>(1),parent=c.Prop<long>(2);
            if(clusters.ContainsKey(parent)&&names.TryGetValue(child,out var name))bone[parent]=name;
            if(clusters.ContainsKey(child))skinOf[child]=parent;
            if(geometry.ContainsKey(parent))geometryOf[child]=parent;
        }
        var links=clusters.Where(c=>bone.ContainsKey(c.Key)).GroupBy(c=>bone[c.Key]).ToDictionary(g=>g.Key,g=>g.First().Value.Link,StringComparer.OrdinalIgnoreCase);
        var result=new List<JointProfile>();
        foreach(string side in new[]{"L","R"})
        foreach(var (name,above,joint,below,prefix,end) in new[]{
            ("Knee","leg_upper_"+side+"_twist0","leg_lower_"+side+"_twist0","ankle_"+side,"leg_","ankle_"+side),
            ("Ankle","leg_lower_"+side+"_twist0","ankle_"+side,"","leg_lower_","ankle_"+side),
            ("Elbow","arm_upper_"+side+"_twist0","arm_lower_"+side+"_twist0","hand_"+side,"arm_","hand_"+side)})
        {
            if(!links.TryGetValue(above,out var a)||!links.TryGetValue(joint,out var j))continue;
            var b=below.Length>0&&links.TryGetValue(below,out var next)?next:j+(j-a);
            float length=Vector3.Distance(j,b);if(length<1e-4f)continue;
            bool Member(string n)=>n.Equals(end,StringComparison.OrdinalIgnoreCase)||prefix.StartsWith("leg_")&&n.StartsWith("ball_"+side,StringComparison.OrdinalIgnoreCase)
                ||n.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&n.EndsWith("_"+side,StringComparison.OrdinalIgnoreCase)
                ||n.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&n.Contains("_"+side+"_",StringComparison.OrdinalIgnoreCase);
            var members=links.Keys.Where(Member).OrderBy(n=>n,StringComparer.Ordinal).ToArray();
            var sums=new double[2*JointProfile.Half+1,members.Length];
            var vertexSums=new Dictionary<(long Geometry,int Vertex),double[]>();
            foreach(var (cluster,data) in clusters)
            {
                if(!bone.TryGetValue(cluster,out var bn)||Array.IndexOf(members,bn) is var m&&m<0)continue;
                if(!skinOf.TryGetValue(cluster,out var skin)||!geometryOf.TryGetValue(skin,out var geo))continue;
                for(int k=0;k<data.Index.Length;k++)
                {
                    var key=(geo,data.Index[k]);
                    if(!vertexSums.TryGetValue(key,out var row))vertexSums[key]=row=new double[members.Length];
                    row[m]+=data.Weight[k];
                }
            }
            foreach(var ((geo,v),row) in vertexSums)
            {
                var p=geometry[geo];var x=new Vector3((float)p[v*3],(float)p[v*3+1],(float)p[v*3+2]);
                if(Math.Min(Vector3.Distance(x,Geometry.ClosestOnSegment(x,a,j)),Vector3.Distance(x,Geometry.ClosestOnSegment(x,j,b)))>length*.35f)continue;
                float u=Position(x,a,j,b,length);
                int sample=(int)MathF.Round(u/JointProfile.Step)+JointProfile.Half;
                if(sample<0||sample>2*JointProfile.Half)continue;
                for(int m=0;m<members.Length;m++)sums[sample,m]+=row[m];
            }
            var shares=new float[2*JointProfile.Half+1][];
            for(int s=0;s<shares.Length;s++)
            {
                double total=Enumerable.Range(0,members.Length).Sum(m=>sums[s,m]);
                shares[s]=Enumerable.Range(0,members.Length).Select(m=>total>0?(float)(sums[s,m]/total):0).ToArray();
            }
            // Fill samples no vertex reached from their nearest measured neighbour.
            for(int s=0;s<shares.Length;s++)if(shares[s].Sum()==0)
            {
                int nearest=Enumerable.Range(0,shares.Length).Where(t=>shares[t].Sum()>0).OrderBy(t=>Math.Abs(t-s)).DefaultIfEmpty(-1).First();
                if(nearest>=0)shares[s]=shares[nearest];
            }
            if(shares.All(s=>s.Sum()>0))result.Add(new JointProfile(name+"."+side,above,joint,below,members,shares));
        }
        return result.ToArray();
    }

    /// <summary>The stock skinned mesh in reference space: vertices, triangles and each vertex's weights,
    /// indexed into <paramref name="bones"/> (the reference armature). Clusters on bones not in the
    /// armature are dropped; each vertex's weights are normalised.</summary>
    public static ReferenceSurface Surface(byte[] fbx,RigBone[] bones)
    {
        var root=FbxTokenizer.Parse(fbx);var objects=root.Child("Objects")??throw new FormatException("The reference has no objects.");
        var names=new Dictionary<long,string>();var geometry=new Dictionary<long,(double[] Points,int[] Polygons)>();
        var clusters=new Dictionary<long,(int[] Index,double[] Weight)>();
        foreach(var node in objects.Children)
        {
            if(node.Properties.Count<2)continue;
            long id=node.Prop<long>(0);names[id]=FbxNode.SplitName(node.AsString(1)).Name;
            if(node.Name=="Geometry"&&node.Child("Vertices") is {} points&&node.Child("PolygonVertexIndex") is {} polygons)geometry[id]=(points.AsDoubleArray(0),polygons.AsIntArray(0));
            if(node.Name=="Deformer"&&node.Properties.Count>2&&node.AsString(2)=="Cluster"&&node.Child("Indexes") is {} indexes&&node.Child("Weights") is {} weights)
                clusters[id]=(indexes.AsIntArray(0),weights.AsDoubleArray(0));
        }
        var bone=new Dictionary<long,int>();var skinOf=new Dictionary<long,long>();var geometryOf=new Dictionary<long,long>();
        foreach(var c in root.Child("Connections")?.ChildrenNamed("C")??[])
        {
            if(c.Properties.Count<3)continue;
            long child=c.Prop<long>(1),parent=c.Prop<long>(2);
            if(clusters.ContainsKey(parent)&&names.TryGetValue(child,out var name)&&Array.FindIndex(bones,b=>b.Name.Equals(name,StringComparison.OrdinalIgnoreCase)) is var index&&index>=0)bone[parent]=index;
            if(clusters.ContainsKey(child))skinOf[child]=parent;
            if(geometry.ContainsKey(parent))geometryOf[child]=parent;
        }
        var vertices=new List<Vector3>();var triangles=new List<int>();var influences=new List<Dictionary<int,float>>();
        var offsets=new Dictionary<long,int>();
        foreach(var (id,(points,polygons)) in geometry)
        {
            offsets[id]=vertices.Count;
            for(int i=0;i+2<points.Length;i+=3){vertices.Add(new Vector3((float)points[i],(float)points[i+1],(float)points[i+2]));influences.Add(new());}
            var polygon=new List<int>();
            foreach(int raw in polygons)
            {
                polygon.Add(offsets[id]+(raw<0?~raw:raw));
                if(raw>=0)continue;
                for(int k=1;k+1<polygon.Count;k++){triangles.Add(polygon[0]);triangles.Add(polygon[k]);triangles.Add(polygon[k+1]);}
                polygon.Clear();
            }
        }
        foreach(var (id,(index,weight)) in clusters)
        {
            if(!bone.TryGetValue(id,out int b)||!skinOf.TryGetValue(id,out var skin)||!geometryOf.TryGetValue(skin,out var geo))continue;
            for(int k=0;k<index.Length;k++)
            {
                int v=offsets[geo]+index[k];if(v<influences.Count&&weight[k]>0)influences[v][b]=influences[v].GetValueOrDefault(b)+(float)weight[k];
            }
        }
        var result=influences.Select(d=>{float total=d.Values.Sum();return total>0?d.Select(p=>new Influence(p.Key,p.Value/total)).OrderByDescending(w=>w.Weight).ToArray():[];}).ToArray();
        if(triangles.Count==0||result.All(r=>r.Length==0))throw new FormatException("The reference has no skinned surface.");
        return new ReferenceSurface(vertices.ToArray(),triangles.ToArray(),result);
    }

    /// <summary>Position along a limb in lower-limb lengths from the joint: negative above, positive below.</summary>
    public static float Position(Vector3 x,Vector3 above,Vector3 joint,Vector3 below,float length)
    {
        var down=Vector3.Normalize(below-joint);var up=Vector3.Normalize(joint-above);var d=x-joint;
        float along=Vector3.Dot(d,down);
        return along>0?along/length:Vector3.Dot(d,up)/length;
    }
}
