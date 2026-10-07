#nullable enable annotations
namespace HumanoidRigger.EditorTools.Core.Analysis;
using Vector3=System.Numerics.Vector3;
using Vector2=System.Numerics.Vector2;

/// <summary>Measures where an arm leaves the torso. The armpit is the apex of the
/// open space below the arm in the frontal silhouette, so the result does not
/// depend on mesh connectivity, vertex density or closed cross sections.</summary>
internal sealed record ArmSocket(Vector3 Armpit,Vector3 Center,Vector3 Axis,float Radius,float Length)
{
    /// <summary>Wrist joint, when the rig knows it; the forearm need not follow the upper arm's line.</summary>
    internal Vector3? Wrist{get;init;}
    /// <summary>The upper arm's own radius along its shaft, when the rig has
    /// measured it. The armpit is the narrowest place an arm leaves the torso;
    /// the shoulder it hangs from is as broad as the arm itself.</summary>
    internal float Girth{get;init;}
    float Height{get;init;}
    /// <summary>How far from its centerline the arm past the shoulder still
    /// reaches, in units of its own radius. A hand is never thinner than a
    /// twentieth of the height across, whatever rod carries it.</summary>
    float Reach(Vector3 point)=>FromChain(point)/Math.Max(Radius,Height*.025f);
    /// <summary>Distance from the arm's own centerline, shoulder to elbow to a
    /// hand's length past the wrist. Far from that line a point is not the arm,
    /// however far along the axis it lies: for a hanging arm the legs do.</summary>
    float FromChain(Vector3 point)
    {
        var elbow=Center+Axis*Length;float upper=Vector3.Distance(point,Geometry.ClosestOnSegment(point,Center,elbow));
        var forearm=Wrist is {} wrist&&Vector3.DistanceSquared(wrist,elbow)>1e-8f?Vector3.Normalize(wrist-elbow):Axis;
        float reach=Wrist is {} known?Vector3.Distance(known,elbow)+Radius*4:Length;
        return Math.Min(upper,Vector3.Distance(point,Geometry.ClosestOnSegment(point,elbow,elbow+forearm*reach)));
    }
    // Frontal occupancy retained from the measurement.
    FrontSilhouette silhouette;
    /// <summary>The axis across the body: outward, level. A lowered arm's axis
    /// points at the floor, and the shoulder above its joint is not behind the arm.</summary>
    Vector3 Lateral
    {
        get
        {
            var flat=new Vector3(Axis.X,0,Axis.Z);
            if(flat.LengthSquared()>.04f)return Vector3.Normalize(flat);
            float outward=Math.Abs(Center.X-Armpit.X)>1e-6f?Center.X-Armpit.X:Axis.X;
            return new(outward<0?-1:1,0,0);
        }
    }
    /// <summary>How far a point lies out from the joint. The trunk begins medial
    /// of the joint, not behind it along the axis: the deltoid over a lowered
    /// arm's joint follows the arm, up to a radius and a half above it.</summary>
    internal float Forward(Vector3 point)
    {
        var offset=point-Center;
        return Math.Min(Vector3.Dot(offset,Lateral),Vector3.Dot(offset,Axis)+Radius*1.5f);
    }
    /// <summary>How much of a point belongs to the arm itself: distal of the
    /// cross section through the joint, and not separated from the arm's axis
    /// by open air. A flank below the armpit faces the arm across that gap,
    /// whereas a muscle or sleeve of any thickness never does.</summary>
    /// <param name="detached">The point lies on a shell of its own, away from the
    /// trunk. Past the upper arm such a shell is a glove, a gauntlet or a claw of
    /// any size, never the legs the arm's axis may point at, so its distance from
    /// the arm's centerline says nothing.</param>
    internal float Support(Vector3 point,bool detached=false)
    {
        var offset=point-Center;float along=Vector3.Dot(offset,Axis);
        // A bent forearm leaves the line of the upper arm. Everything past
        // the upper arm, and near the arm itself, is arm; only the socket
        // region needs separating.
        float beyond=Smooth((along/Length-.7f)/.3f);
        if(beyond>0&&!detached)beyond*=1-Smooth((Reach(point)-1.6f)/.7f);
        if(beyond>=1)return 1;
        float radial=(offset-Axis*along).Length();
        // The trunk gives way to the arm over the breadth of the shoulder, a
        // radius and a fifth wide, measured from the arm's own girth where a
        // bodybuilder's deltoid dwarfs his armpit.
        float breadth=Math.Max(Radius,Girth);
        float support=Smooth((Forward(point)/breadth+.6f)/1.2f)*(1-Smooth((radial/Radius-1.6f)/.7f));
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
    internal float Beyond(Vector3 point)
    {
        // Only within the arm's own thickness. A hanging arm points at the
        // floor, so the hips lie far along its axis without being any part of it.
        float along=Vector3.Dot(point-Center,Axis);
        return Support(point)*Smooth((along/Radius-.5f)/1.5f)*(1-Smooth((Reach(point)-1.6f)/.7f));
    }
    /// <summary>The shoulder girdle carries the socket: neither the arm beyond
    /// it nor the flank below the armpit.</summary>
    /// <remarks>The girdle ends within half a radius of the socket, and on a
    /// thick arm no farther than an eighth of the upper arm: a thick arm is not
    /// a longer shoulder.</remarks>
    internal float Girdle(Vector3 point)
    {
        float along=Vector3.Dot(point-Center,Axis);
        float reach=Math.Min(1-Smooth((along/Radius-.5f)/1.5f),1-Smooth((along/Length-.12f)/.23f));
        return reach*Smooth((point.Y-Armpit.Y)/Radius+.25f);
    }
    static float Smooth(float t){t=Math.Clamp(t,0,1);return t*t*(3-2*t);}

    /// <summary>Shoulders are level. A pouch, a holster or a hand on the hip fills
    /// the space under one lowered arm, and the silhouette then offers a corner
    /// well below the armpit: the lower of two sockets rises to the other.</summary>
    internal static (ArmSocket? Left,ArmSocket? Right) Level(ArmSocket? left,ArmSocket? right,Vector3 leftElbow,Vector3 rightElbow,float height)
    {
        if(left is null||right is null)return(left,right);
        float gap=left.Center.Y-right.Center.Y;
        if(Math.Abs(gap)<=height*.03f)return(left,right);
        return gap>0?(left,right.Raise(gap,rightElbow)):(left.Raise(-gap,leftElbow),right);
    }
    ArmSocket Raise(float by,Vector3 elbow)
    {
        var center=Center+Vector3.UnitY*by;var direction=elbow-center;
        if(direction.LengthSquared()<1e-8f)return this;
        return this with{Armpit=Armpit+Vector3.UnitY*by,Center=center,Axis=Vector3.Normalize(direction),Length=direction.Length()};
    }

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
        var regions=window.Regions((x,y)=>
        {
            if(Filled(x,y))return false;
            float lateral=sign*(left+(x+.5f)*cell-centerX);if(lateral<height*.03f||lateral>reach)return false;
            int inward=1;while(inward<=limit&&!Filled(x+medial*inward,y)&&sign*(left+(x+medial*inward+.5f)*cell-centerX)>0)inward++;
            if(!Filled(x+medial*inward,y))return false;
            int upward=1;while(upward<=limit&&y+upward<window.Rows&&!Filled(x,y+upward))upward++;
            return Filled(x,y+upward);
        }).Where(region=>region.Count>=40).ToList();
        // The space opens between the torso side, which runs downward, and the
        // underside of the arm. Its apex lies farthest against their bisector.
        var along=new Vector2(elbow.X-shoulder.X,elbow.Y-shoulder.Y);if(along.LengthSquared()<1e-8f)return null;
        var opening=Vector2.Normalize(along)-Vector2.UnitY;if(opening.LengthSquared()<1e-4f)return null;opening=Vector2.Normalize(opening);
        int Apex(List<int> region)=>region.MinBy(i=>(i%nx)*opening.X+(i/nx)*opening.Y);
        // An armpit lies below the shoulder. The corner beside the neck under a
        // hat brim or a hood does not.
        regions.RemoveAll(region=>bottom+(Apex(region)/nx+1)*cell>shoulder.Y+height*.02f);
        if(regions.Count==0)return null;
        var owner=new int[nx*window.Rows];Array.Fill(owner,-1);
        for(int r=0;r<regions.Count;r++)foreach(int i in regions[r])owner[i]=r;
        var gap=regions.MaxBy(region=>region.Count)!;
        // A pouch, a holster or a hand on the hip splits the space below a
        // lowered arm, and the larger part may lie beneath it. The armpit
        // tops the whole stack: climb through whatever sits over the space.
        for(int hops=0;hops<4;hops++)
        {
            var top=new Dictionary<int,int>();
            foreach(int i in gap){int x=i%nx,y=i/nx;top[x]=Math.Max(y,top.GetValueOrDefault(x,-1));}
            var votes=new Dictionary<int,int>();
            foreach(var (x,y) in top)
            {
                int up=y+1;while(up<window.Rows&&Filled(x,up))up++;
                if(up>=window.Rows||up-y>limit)continue;
                int above=owner[x+nx*up];
                if(above>=0&&regions[above]!=gap)votes[above]=votes.GetValueOrDefault(above)+1;
            }
            if(votes.Count==0)break;
            gap=regions[votes.MaxBy(vote=>vote.Value).Key];
        }
        int apex=Apex(gap);
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
                // The seed axis only points the walk. The joint keeps a radius
                // inside the arm's own outer edge, up the slant of an upper arm
                // the estimate never saw, a cell at a time.
                int outer=0;while(outer<limit&&Inside(next+normal*((outer+1)*cell)))outer++;
                if(outer<limit)next+=normal*Math.Clamp(outer*cell-radius,-cell,cell);
                if(!Inside(next)||!Inside(next+Vector2.UnitY*radius*.9f))break;
                center=next;
            }
        }
        float depth=window.Depth(center)??shoulder.Z;
        var direction=elbow-new Vector3(center,depth);if(direction.LengthSquared()<1e-8f)return null;
        return new(new(armpit,depth),new(center,depth),Vector3.Normalize(direction),radius,direction.Length()){silhouette=window,Height=height};
    }
}
