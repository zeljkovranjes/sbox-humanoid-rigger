using System.Numerics;
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Orthographic hand crops with actual front/back mesh depth, independent of the editor viewport.</summary>
public sealed class HandView
{
    public const int Resolution=224;
    public Vector3 Center {get;init;}
    public Vector3 Right {get;init;}
    public Vector3 Up {get;init;}
    public Vector3 Normal {get;init;}
    public float Extent {get;init;}
    public float[] Rgb {get;}=new float[Resolution*Resolution*3];
    readonly float[] front=Enumerable.Repeat(float.NegativeInfinity,Resolution*Resolution).ToArray();
    readonly float[] back=Enumerable.Repeat(float.PositiveInfinity,Resolution*Resolution).ToArray();
    public Vector3 Project(Vector3 point)
    {
        var d=point-Center;
        return new((Vector3.Dot(d,Right)/Extent+.5f)*Resolution,(.5f-Vector3.Dot(d,Up)/Extent)*Resolution,Vector3.Dot(d,Normal));
    }
    public bool TryLift(Vector3 pixel,out Vector3 point)
    {
        point=default;if(!Geometry.Finite(pixel))return false;
        int x=(int)MathF.Floor(pixel.X),y=(int)MathF.Floor(pixel.Y);
        if(x<0||x>=Resolution||y<0||y>=Resolution)return false;
        int best=-1;float distance=10;
        // Small gaps between separately modelled phalanges are allowed. Larger silhouette misses are rejected.
        for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++)
        {
            int px=x+dx,py=y+dy;if(px<0||px>=Resolution||py<0||py>=Resolution)continue;
            int index=py*Resolution+px;float d=dx*dx+dy*dy;
            if(d<distance&&float.IsFinite(front[index])){best=index;distance=d;}
        }
        if(best<0)return false;
        float depth=(front[best]+back[best])*.5f;
        point=Center+Right*((pixel.X/Resolution-.5f)*Extent)+Up*((.5f-pixel.Y/Resolution)*Extent)+Normal*depth;
        return true;
    }
    public static HandView[] Render(ImportedCharacter character,Anatomy anatomy,string side)
    {
        if(!anatomy.Hands.TryGetValue(side,out var frame))return [];
        var wrist=anatomy["Hand."+side];float height=anatomy.Height;
        bool InHand(Vector3 p)=>Vector3.Distance(p,wrist)<height*.16f&&Vector3.Dot(p-wrist,frame.Forward)>-height*.025f;
        var parts=character.Meshes.Where(m=>m.Kind==MeshKind.Body).ToArray();
        var vertices=parts.SelectMany(m=>m.Vertices).Where(InHand).ToArray();if(vertices.Length<12)return [];
        var result=new List<HandView>();
        foreach(float degrees in new[]{0f,35f,-35f,180f,145f,215f})
        {
            var normal=Vector3.Transform(frame.Normal,Quaternion.CreateFromAxisAngle(frame.Forward,degrees*MathF.PI/180));
            var right=Vector3.Normalize(Vector3.Cross(frame.Forward,normal));var up=Vector3.Normalize(Vector3.Cross(normal,right));
            float xmin=vertices.Min(p=>Vector3.Dot(p-wrist,right)),xmax=vertices.Max(p=>Vector3.Dot(p-wrist,right));
            float ymin=vertices.Min(p=>Vector3.Dot(p-wrist,up)),ymax=vertices.Max(p=>Vector3.Dot(p-wrist,up));
            var view=new HandView{Normal=normal,Up=up,Right=right,Center=wrist+right*((xmin+xmax)*.5f)+up*((ymin+ymax)*.5f),Extent=Math.Max(xmax-xmin,ymax-ymin)*1.25f};
            Array.Fill(view.Rgb,.08f);
            foreach(var part in parts)
            {
                var normals=new Vector3[part.Vertices.Length];
                for(int t=0;t<part.Triangles.Length;t+=3)
                {
                    int a=part.Triangles[t],b=part.Triangles[t+1],c=part.Triangles[t+2];
                    var n=Vector3.Cross(part.Vertices[b]-part.Vertices[a],part.Vertices[c]-part.Vertices[a]);normals[a]+=n;normals[b]+=n;normals[c]+=n;
                }
                for(int i=0;i<normals.Length;i++)normals[i]=normals[i].LengthSquared()>1e-12f?Vector3.Normalize(normals[i]):normal;
                for(int t=0;t<part.Triangles.Length;t+=3)
                {
                    int a=part.Triangles[t],b=part.Triangles[t+1],c=part.Triangles[t+2];
                    if(!InHand(part.Vertices[a])||!InHand(part.Vertices[b])||!InHand(part.Vertices[c]))continue;
                    view.Triangle(view.Project(part.Vertices[a]),view.Project(part.Vertices[b]),view.Project(part.Vertices[c]),normals[a],normals[b],normals[c]);
                }
            }
            result.Add(view);
        }
        return result.ToArray();
    }
    void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector3 na,Vector3 nb,Vector3 nc)
    {
        static float Edge(Vector3 p,Vector3 q,float x,float y)=>(x-p.X)*(q.Y-p.Y)-(y-p.Y)*(q.X-p.X);
        float area=Edge(a,b,c.X,c.Y);if(Math.Abs(area)<1e-6f)return;
        int xmin=Math.Max(0,(int)MathF.Floor(Math.Min(a.X,Math.Min(b.X,c.X)))),xmax=Math.Min(Resolution-1,(int)MathF.Ceiling(Math.Max(a.X,Math.Max(b.X,c.X))));
        int ymin=Math.Max(0,(int)MathF.Floor(Math.Min(a.Y,Math.Min(b.Y,c.Y)))),ymax=Math.Min(Resolution-1,(int)MathF.Ceiling(Math.Max(a.Y,Math.Max(b.Y,c.Y))));
        var light=Vector3.Normalize(Normal+Up*.4f+Right*.35f);
        for(int y=ymin;y<=ymax;y++)for(int x=xmin;x<=xmax;x++)
        {
            float u=Edge(b,c,x+.5f,y+.5f)/area,v=Edge(c,a,x+.5f,y+.5f)/area,w=1-u-v;
            if(u<0||v<0||w<0)continue;
            int index=y*Resolution+x;float z=u*a.Z+v*b.Z+w*c.Z;back[index]=Math.Min(back[index],z);
            if(z<=front[index])continue;front[index]=z;
            var n=na*u+nb*v+nc*w;if(n.LengthSquared()>1e-12f)n=Vector3.Normalize(n);
            float shade=.45f+.5f*Math.Abs(Vector3.Dot(n,light));
            Rgb[index*3]=shade*.78f;Rgb[index*3+1]=shade*.68f;Rgb[index*3+2]=shade*.59f;
        }
    }
}
