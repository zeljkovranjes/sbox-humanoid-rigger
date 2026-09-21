#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;
using Vector2=System.Numerics.Vector2;

/// <summary>Measures where an arm leaves the torso. The armpit is the apex of the
/// open space below the arm in the frontal silhouette, so the result does not
/// depend on mesh connectivity, vertex density or closed cross sections.</summary>
internal sealed record ArmSocket(Vector3 Armpit,Vector3 Center,Vector3 Axis,float Radius,float Length)
{
    // Frontal occupancy retained from the measurement.
    bool[] filled=[];int nx,ny;float left,bottom,cell;
    /// <summary>How much of a point belongs to the arm itself: distal of the
    /// cross section through the joint, and not separated from the arm's axis
    /// by open air. A flank below the armpit faces the arm across that gap,
    /// whereas a muscle or sleeve of any thickness never does.</summary>
    internal float Support(Vector3 point)
    {
        var offset=point-Center;float along=Vector3.Dot(offset,Axis);
        // A bent forearm leaves the line of the upper arm. Everything past
        // the upper arm is arm; only the socket region needs separating.
        float beyond=Smooth((along/Length-.7f)/.3f);
        if(beyond>=1)return 1;
        float radial=(offset-Axis*along).Length();
        float support=Smooth((along/Radius+.6f)/1.2f)*(1-Smooth((radial/Radius-1.6f)/.7f));
        support+=(1-support)*beyond;
        if(support<=0)return 0;
        // The open space below the arm is a wedge with its apex at the armpit,
        // between the torso side and the arm's underside. Past that apex, the
        // torso's side of the bisector is flank however close the arm is.
        var lateral=new Vector2(Axis.X,Axis.Y);
        if(lateral.LengthSquared()>1e-6f)
        {
            var bisector=Vector2.Normalize(lateral)-Vector2.UnitY;
            if(bisector.LengthSquared()>1e-6f)
            {
                bisector=Vector2.Normalize(bisector);
                // A hanging arm has almost no sideways axis; its centerline
                // still lies outward of the armpit.
                float outward=Math.Abs(Axis.X)>.2f?Axis.X:Center.X-Armpit.X;
                var torso=new Vector2(bisector.Y,-bisector.X);if(torso.X*outward>0)torso=-torso;
                var fromApex=new Vector2(point.X-Armpit.X,point.Y-Armpit.Y);
                // Surface well inside the torso's side counts as past the apex
                // even when it is level with it, as on the back below a shoulder.
                float inward=Vector2.Dot(fromApex,torso),past=Vector2.Dot(fromApex,bisector)+.5f*Math.Max(0,inward-Radius*.3f);
                float flank=Smooth(past/(Radius*.3f))*Smooth((inward/Radius+.15f)/.3f);
                support*=1-flank*(1-beyond);
                if(support<=0)return 0;
            }
        }
        var target=Center+Axis*Math.Max(along,0);
        var from=new Vector2(point.X,point.Y);var to=new Vector2(target.X,target.Y);
        float length=Vector2.Distance(from,to);int samples=(int)MathF.Ceiling(length/(cell*.5f));
        if(samples<2)return support;
        int open=0;
        for(int i=1;i<samples;i++)
        {
            var p=Vector2.Lerp(from,to,i/(float)samples);
            int x=(int)MathF.Floor((p.X-left)/cell),y=(int)MathF.Floor((p.Y-bottom)/cell);
            if(x>=0&&y>=0&&x<nx&&y<ny&&!filled[x+nx*y])open++;
        }
        float separated=Smooth((length*open/samples/Radius-.1f)/.3f)*(1-beyond);
        return support*(1-separated);
    }
    /// <summary>The shoulder girdle carries the socket: neither the arm beyond
    /// it nor the flank below the armpit.</summary>
    internal float Girdle(Vector3 point)=>(1-Smooth((Vector3.Dot(point-Center,Axis)/Radius-.5f)/1.5f))*Smooth((point.Y-Armpit.Y)/Radius+.25f);
    static float Smooth(float t){t=Math.Clamp(t,0,1);return t*t*(3-2*t);}

    internal static ArmSocket? Measure(IEnumerable<MeshPart> meshes,Vector3 shoulder,Vector3 elbow,float centerX,float height)
    {
        float sign=Math.Sign(shoulder.X-centerX);if(sign==0||height<=0)return null;
        float cell=height/360,reach=Math.Abs(elbow.X-centerX);
        float left=Math.Min(centerX,centerX+sign*(reach+height*.08f)),bottom=shoulder.Y-height*.3f;
        int nx=(int)MathF.Ceiling((reach+height*.08f)/cell)+2,ny=(int)MathF.Ceiling(height*.45f/cell)+2;
        var filled=new bool[nx*ny];var near=new float[nx*ny];var far=new float[nx*ny];
        Array.Fill(near,float.PositiveInfinity);Array.Fill(far,float.NegativeInfinity);
        void Mark(int x,int y,float z)
        {
            if(x<0||y<0||x>=nx||y>=ny)return;
            int i=x+nx*y;filled[i]=true;near[i]=Math.Min(near[i],z);far[i]=Math.Max(far[i],z);
        }
        foreach(var mesh in meshes)for(int t=0;t<mesh.Triangles.Length;t+=3)
        {
            var a=mesh.Vertices[mesh.Triangles[t]];var b=mesh.Vertices[mesh.Triangles[t+1]];var c=mesh.Vertices[mesh.Triangles[t+2]];
            var low=Vector3.Min(a,Vector3.Min(b,c));var high=Vector3.Max(a,Vector3.Max(b,c));
            if(high.X<left||low.X>left+nx*cell||high.Y<bottom||low.Y>bottom+ny*cell)continue;
            // Sample densely enough that thin or edge-on faces still cover
            // their cells; a missed cell would read as open space.
            int divisions=Math.Clamp((int)MathF.Ceiling(Math.Max(high.X-low.X,high.Y-low.Y)/(cell*.5f)),1,256);
            for(int u=0;u<=divisions;u++)for(int v=0;v<=divisions-u;v++)
            {
                var p=a+(b-a)*(u/(float)divisions)+(c-a)*(v/(float)divisions);
                Mark((int)MathF.Floor((p.X-left)/cell),(int)MathF.Floor((p.Y-bottom)/cell),p.Z);
            }
        }
        bool Filled(int x,int y)=>x>=0&&y>=0&&x<nx&&y<ny&&filled[x+nx*y];
        int medial=sign>0?-1:1,limit=(int)(height*.3f/cell);
        // Open cells enclosed by the torso on the medial side and by the arm
        // above. Their corner is the armpit for raised and lowered arms alike.
        var below=new bool[nx*ny];
        for(int y=0;y<ny;y++)for(int x=0;x<nx;x++)
        {
            if(filled[x+nx*y])continue;
            float lateral=sign*(left+(x+.5f)*cell-centerX);if(lateral<height*.03f||lateral>reach)continue;
            int inward=1;while(inward<=limit&&!Filled(x+medial*inward,y)&&sign*(left+(x+medial*inward+.5f)*cell-centerX)>0)inward++;
            if(!Filled(x+medial*inward,y))continue;
            int upward=1;while(upward<=limit&&y+upward<ny&&!Filled(x,y+upward))upward++;
            if(!Filled(x,y+upward))continue;
            below[x+nx*y]=true;
        }
        var seen=new bool[nx*ny];List<int>? gap=null;
        for(int start=0;start<below.Length;start++)
        {
            if(!below[start]||seen[start])continue;
            var component=new List<int>();var queue=new Queue<int>();queue.Enqueue(start);seen[start]=true;
            while(queue.TryDequeue(out int i))
            {
                component.Add(i);int x=i%nx,y=i/nx;
                foreach(var (dx,dy) in new[]{(1,0),(-1,0),(0,1),(0,-1)})
                {
                    int px=x+dx,py=y+dy;if(px<0||py<0||px>=nx||py>=ny)continue;
                    int n=px+nx*py;if(!below[n]||seen[n])continue;seen[n]=true;queue.Enqueue(n);
                }
            }
            if(gap is null||component.Count>gap.Count)gap=component;
        }
        if(gap is null||gap.Count<40)return null;
        // The space opens between the torso side, which runs downward, and the
        // underside of the arm. Its apex lies farthest against their bisector.
        var along=new Vector2(elbow.X-shoulder.X,elbow.Y-shoulder.Y);if(along.LengthSquared()<1e-8f)return null;
        var opening=Vector2.Normalize(along)-Vector2.UnitY;if(opening.LengthSquared()<1e-4f)return null;opening=Vector2.Normalize(opening);
        int apex=gap.MinBy(i=>(i%nx)*opening.X+(i/nx)*opening.Y);
        var armpit=new Vector2(left+(apex%nx+.5f+medial*.5f)*cell,bottom+(apex/nx+1)*cell);
        if(sign*(armpit.X-centerX)<height*.04f||sign*(armpit.X-centerX)>height*.22f)return null;
        var center=armpit;var axis=Vector2.Zero;float radius=0;
        var target=new Vector2(elbow.X,elbow.Y);
        bool Inside(Vector2 p)=>Filled((int)MathF.Floor((p.X-left)/cell),(int)MathF.Floor((p.Y-bottom)/cell));
        for(int pass=0;pass<3;pass++)
        {
            axis=target-(pass==0?new Vector2(shoulder.X,shoulder.Y):center);
            if(axis.LengthSquared()<1e-8f)return null;axis=Vector2.Normalize(axis);
            // Across the arm, away from the torso: upward for a raised arm and
            // outward for one hanging beside the body.
            var normal=new Vector2(-axis.Y,axis.X);if(Vector2.Dot(normal,new(sign,1))<0)normal=-normal;
            int steps=0,open=0,last=0;
            while(open<3&&steps<limit)
            {
                steps++;
                if(Inside(armpit+normal*(steps*cell))){open=0;last=steps;}else open++;
            }
            radius=last*cell*.5f;
            if(radius<height*.0125f||radius>height*.1f)return null;
            // The joint sits on the arm's centerline above the armpit. A lowered
            // arm meets that line higher up, but never closer than its own
            // radius to the top of the shoulder.
            center=armpit+normal*radius;
            while(sign*(center.X-armpit.X)>0&&axis.Y<-.05f)
            {
                var next=center-axis*cell;
                if(!Inside(next)||!Inside(next+Vector2.UnitY*radius*.9f))break;
                center=next;
            }
        }
        int cx=(int)MathF.Floor((center.X-left)/cell),cy=(int)MathF.Floor((center.Y-bottom)/cell);
        float depth=Filled(cx,cy)?(near[cx+nx*cy]+far[cx+nx*cy])*.5f:shoulder.Z;
        var direction=elbow-new Vector3(center,depth);if(direction.LengthSquared()<1e-8f)return null;
        return new(new(armpit,depth),new(center,depth),Vector3.Normalize(direction),radius,direction.Length()){filled=filled,nx=nx,ny=ny,left=left,bottom=bottom,cell=cell};
    }
}
