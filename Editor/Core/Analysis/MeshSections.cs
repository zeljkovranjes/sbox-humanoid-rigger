namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;
using Vector2=System.Numerics.Vector2;

/// <summary>Closed triangle-plane contours, with area centers independent of vertex density.</summary>
internal static class MeshSections
{
    internal record Section(Vector3 Center,float Area,float Radius,float MinimumRadius);
    internal static Section[] Cut(ImportedCharacter character,Vector3 origin,Vector3 normal,float reach,float tolerance)
    {
        normal=Vector3.Normalize(normal);var u=Vector3.Normalize(Vector3.Cross(normal,Math.Abs(normal.Z)>.9f?Vector3.UnitY:Vector3.UnitZ));var v=Vector3.Cross(normal,u);
        var points=new List<Vector2>();var edges=new HashSet<(int,int)>();var cells=new Dictionary<(int,int),List<int>>();
        int Node(Vector3 position)
        {
            var delta=position-origin;var p=new Vector2(Vector3.Dot(delta,u),Vector3.Dot(delta,v));
            int x=(int)Math.Floor(p.X/tolerance),y=(int)Math.Floor(p.Y/tolerance);
            for(int i=-1;i<=1;i++)for(int j=-1;j<=1;j++)if(cells.TryGetValue((x+i,y+j),out var nearby))
                foreach(int n in nearby)if(Vector2.DistanceSquared(points[n],p)<=tolerance*tolerance)return n;
            int index=points.Count;points.Add(p);if(!cells.TryGetValue((x,y),out var bucket))cells[(x,y)]=bucket=[];bucket.Add(index);return index;
        }
        var cut=new Vector3[3];
        foreach(var mesh in character.Meshes.Where(m=>m.Kind==MeshKind.Body))for(int t=0;t<mesh.Triangles.Length;t+=3)
        {
            int count=0;
            for(int e=0;e<3;e++)
            {
                var a=mesh.Vertices[mesh.Triangles[t+e]];var b=mesh.Vertices[mesh.Triangles[t+(e+1)%3]];
                float da=Vector3.Dot(a-origin,normal),db=Vector3.Dot(b-origin,normal);
                if((da<=0&&db>0)||(db<=0&&da>0))cut[count++]=Vector3.Lerp(a,b,da/(da-db));
            }
            if(count!=2||Vector3.Distance(cut[0],origin)>reach||Vector3.Distance(cut[1],origin)>reach)continue;
            int first=Node(cut[0]),second=Node(cut[1]);if(first!=second)edges.Add((Math.Min(first,second),Math.Max(first,second)));
        }
        var neighbors=points.Select(_=>new List<int>()).ToArray();foreach(var(a,b)in edges){neighbors[a].Add(b);neighbors[b].Add(a);}
        var sections=new List<Section>();var seen=new bool[points.Count];
        for(int start=0;start<points.Count;start++)
        {
            if(seen[start])continue;var component=new List<int>();var queue=new Queue<int>();queue.Enqueue(start);seen[start]=true;
            while(queue.TryDequeue(out int n)){component.Add(n);foreach(int next in neighbors[n])if(!seen[next]){seen[next]=true;queue.Enqueue(next);}}
            if(component.Count<6||component.Any(n=>neighbors[n].Count!=2))continue;
            var polygon=new List<Vector2>();int previous=-1,current=start;
            do{polygon.Add(points[current]);int next=neighbors[current].First(n=>n!=previous);previous=current;current=next;}while(current!=start&&polygon.Count<=component.Count);
            if(current!=start||polygon.Count!=component.Count)continue;
            float twiceArea=0;var weighted=Vector2.Zero;
            for(int i=0;i<polygon.Count;i++){var a=polygon[i];var b=polygon[(i+1)%polygon.Count];float cross=a.X*b.Y-b.X*a.Y;twiceArea+=cross;weighted+=(a+b)*cross;}
            if(Math.Abs(twiceArea)<tolerance*tolerance)continue;
            var center=weighted/(3*twiceArea);float radius=polygon.Max(p=>Vector2.Distance(center,p));
            float minimum=float.PositiveInfinity;
            for(int i=0;i<polygon.Count;i++)
            {
                var a=polygon[i];var b=polygon[(i+1)%polygon.Count];var ab=b-a;
                float t=Math.Clamp(Vector2.Dot(center-a,ab)/Math.Max(ab.LengthSquared(),1e-12f),0,1);minimum=Math.Min(minimum,Vector2.Distance(center,a+ab*t));
            }
            sections.Add(new(origin+u*center.X+v*center.Y,Math.Abs(twiceArea)*.5f,radius,minimum));
        }
        return sections.ToArray();
    }
}
