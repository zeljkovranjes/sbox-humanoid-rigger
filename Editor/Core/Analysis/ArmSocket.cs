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
    FrontSilhouette silhouette;
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
        float open=silhouette.OpenLength(new(point.X,point.Y),new(target.X,target.Y));
        float separated=Smooth((open/Radius-.1f)/.3f)*(1-beyond);
        return support*(1-separated);
    }
    /// <summary>How far a point lies out along the arm itself. The trunk carries
    /// the socket, not the limb: a spine weight lingering down the arm blends
    /// every vertex there, and blended vertices lose volume when it turns.</summary>
    internal float Beyond(Vector3 point)=>Support(point)*Smooth((Vector3.Dot(point-Center,Axis)/Radius-.5f)/1.5f);
    /// <summary>The shoulder girdle carries the socket: neither the arm beyond
    /// it nor the flank below the armpit.</summary>
    internal float Girdle(Vector3 point)=>(1-Smooth((Vector3.Dot(point-Center,Axis)/Radius-.5f)/1.5f))*Smooth((point.Y-Armpit.Y)/Radius+.25f);
    static float Smooth(float t){t=Math.Clamp(t,0,1);return t*t*(3-2*t);}

    internal static ArmSocket? Measure(IEnumerable<MeshPart> meshes,Vector3 shoulder,Vector3 elbow,float centerX,float height)
    {
        float sign=Math.Sign(shoulder.X-centerX);if(sign==0||height<=0)return null;
        float cell=height/360,reach=Math.Abs(elbow.X-centerX);
        float left=Math.Min(centerX,centerX+sign*(reach+height*.08f)),bottom=shoulder.Y-height*.3f;
        var window=new FrontSilhouette(meshes,left,bottom,reach+height*.08f,height*.45f,cell);int nx=window.Columns;
        bool Filled(int x,int y)=>window.Filled(x,y);
        int medial=sign>0?-1:1,limit=(int)(height*.3f/cell);
        // Open cells enclosed by the torso on the medial side and by the arm
        // above. Their corner is the armpit for raised and lowered arms alike.
        var gap=window.Regions((x,y)=>
        {
            if(Filled(x,y))return false;
            float lateral=sign*(left+(x+.5f)*cell-centerX);if(lateral<height*.03f||lateral>reach)return false;
            int inward=1;while(inward<=limit&&!Filled(x+medial*inward,y)&&sign*(left+(x+medial*inward+.5f)*cell-centerX)>0)inward++;
            if(!Filled(x+medial*inward,y))return false;
            int upward=1;while(upward<=limit&&y+upward<window.Rows&&!Filled(x,y+upward))upward++;
            return Filled(x,y+upward);
        }).MaxBy(region=>region.Count);
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
        bool Inside(Vector2 p)=>window.Inside(p);
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
        float depth=window.Depth(center)??shoulder.Z;
        var direction=elbow-new Vector3(center,depth);if(direction.LengthSquared()<1e-8f)return null;
        return new(new(armpit,depth),new(center,depth),Vector3.Normalize(direction),radius,direction.Length()){silhouette=window};
    }
}
