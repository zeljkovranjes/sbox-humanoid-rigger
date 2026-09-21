#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;
using Vector2=System.Numerics.Vector2;

/// <summary>Measures where a leg leaves the pelvis. The crotch is the top of
/// the open space between the legs in the frontal silhouette. From there the
/// thigh's boundary rises toward the side of the hip, as the groin crease and
/// the buttock do: a level cut through the hip joint instead leaves the whole
/// upper thigh on the pelvis and hinges the leg at the crotch.</summary>
internal sealed record LegSocket(Vector3 Crotch,Vector3 Axis,float Radius,float Side,Vector2 Divide)
{
    // Angle between the leg's reversed axis and its boundary, toward the hip's side.
    const float Rise=50*MathF.PI/180;
    /// <summary>How much of a point belongs to the leg: half at the boundary,
    /// reaching none and all within half a thigh radius on either side.</summary>
    internal float Support(Vector3 point)
    {
        var down=new Vector2(Axis.X,Axis.Y);if(down.LengthSquared()<1e-6f)return 1;
        down=Vector2.Normalize(down);
        // Across the leg, away from the other one. The boundary turns with the
        // leg, so a wide stance keeps the same anatomy.
        var outward=new Vector2(-down.Y,down.X);if(outward.X*Side<0)outward=-outward;
        var below=outward*MathF.Cos(Rise)+down*MathF.Sin(Rise);
        float depth=Vector2.Dot(new(point.X-Crotch.X,point.Y-Crotch.Y),below);
        // The boundary keeps rising outward, more so for a wide stance. A
        // thigh still ends at the top of the pelvis, never at the waist.
        float crest=1-TrunkRegion.Smooth(((point.Y-Crotch.Y)/Radius-1.2f)/.6f);
        return TrunkRegion.Smooth(depth/Radius+.5f)*crest*Inner(point);
    }
    /// <summary>Thighs that touch are close enough to claim one another. The
    /// line from the crotch between the knees divides them; it follows the
    /// stance, where the character's estimated centerline may cut into a leg.</summary>
    internal float Inner(Vector3 point)
    {
        var offset=new Vector2(point.X-Crotch.X,point.Y-Crotch.Y);
        return TrunkRegion.Smooth(Side*(Divide.X*offset.Y-Divide.Y*offset.X)/(Radius*.3f)+.5f);
    }
    static Vector2 Between(Vector3 crotch,Vector3 knee,Vector3 otherKnee)
    {
        var down=new Vector2((knee.X+otherKnee.X)*.5f-crotch.X,(knee.Y+otherKnee.Y)*.5f-crotch.Y);
        return down.LengthSquared()<1e-8f||down.Y>=0?-Vector2.UnitY:Vector2.Normalize(down);
    }
    /// <summary>Thighs that touch leave no open space to see. The legs are still
    /// two closed contours in a level cut until they merge into the pelvis, and
    /// the crotch is where that happens.</summary>
    internal static LegSocket? FromSections(ImportedCharacter surface,Vector3 hip,Vector3 knee,Vector3 otherKnee,float centerX,float height)
    {
        float side=Math.Sign(hip.X-centerX);if(side==0||height<=0||hip.Y<=knee.Y)return null;
        float step=height*.005f,lowest=knee.Y+(hip.Y-knee.Y)*.55f,mirrored=2*centerX-hip.X;
        (MeshSections.Section Own,MeshSections.Section Other)? Legs(float y)
        {
            var legs=MeshSections.Cut(surface,new(centerX,y,hip.Z),Vector3.UnitY,height*.45f,height*1e-5f)
                .Where(s=>s.Area>height*height*.0012f&&s.Area<height*height*.062f).ToArray();
            var own=legs.Where(s=>Math.Abs(s.Center.X-hip.X)<height*.08f).MinBy(s=>Math.Abs(s.Center.X-hip.X));
            if(own is null)return null;
            var other=legs.Where(s=>!ReferenceEquals(s,own)&&Math.Abs(s.Center.X-mirrored)<height*.08f).MinBy(s=>Math.Abs(s.Center.X-mirrored));
            // Legs stand side by side. Contours sharing a center are one body
            // inside its clothing; two on one side are a leg and a holster.
            if(other is null)return null;
            float apart=(own.Center.X-other.Center.X)*side,widths=MathF.Sqrt(own.Area/MathF.PI)+MathF.Sqrt(other.Area/MathF.PI);
            return apart<widths*.6f?null:(own,other);
        }
        (MeshSections.Section Own,MeshSections.Section Other)? above=null;bool merged=false;
        // The crotch lies below the hip joint. Legs already apart at the top of
        // the search never showed where they merge, which is the measurement.
        for(float y=hip.Y+height*.03f;y>=lowest;y-=step)
        {
            var legs=Legs(y);
            if(legs is null)merged=true;
            else if(!merged)return null;
            // One stray pair of contours inside the pelvis is not a pair of legs.
            if(legs is {} found&&above is not null)
            {
                float own=MathF.Sqrt(found.Own.Area/MathF.PI),other=MathF.Sqrt(found.Other.Area/MathF.PI);
                float x=(found.Own.Center.X-side*own+found.Other.Center.X+side*other)*.5f;
                var crotch=new Vector3(x,y+step*1.5f,found.Own.Center.Z);
                return new(crotch,Vector3.Normalize(knee-hip),own,side,Between(crotch,knee,otherKnee));
            }
            above=legs;
        }
        return null;
    }
    internal static LegSocket? Measure(IEnumerable<MeshPart> meshes,Vector3 hip,Vector3 knee,Vector3 otherKnee,float centerX,float height)
    {
        float side=Math.Sign(hip.X-centerX);if(side==0||height<=0||hip.Y<=knee.Y)return null;
        float cell=height/360,reach=height*.3f,bottom=knee.Y;
        var window=new FrontSilhouette(meshes,centerX-reach,bottom,reach*2,hip.Y+height*.1f-bottom,cell);
        int limit=(int)(reach/cell);
        // Open cells with a leg on either side and the pelvis above.
        var gaps=window.Regions((x,y)=>
        {
            if(window.Filled(x,y)||Math.Abs(window.Left+(x+.5f)*cell-centerX)>height*.12f)return false;
            foreach(int direction in new[]{-1,1})
            {
                int steps=1;while(steps<=limit&&!window.Filled(x+direction*steps,y))steps++;
                if(!window.Filled(x+direction*steps,y))return false;
            }
            int upward=1;while(upward<=limit&&y+upward<window.Rows&&!window.Filled(x,y+upward))upward++;
            return window.Filled(x,y+upward);
        });
        // A holster or fold can bridge the thighs and split this space; the
        // crotch closes the highest part. A long coat or skirt instead closes
        // it well below the body: better no measurement than a hem.
        Vector2? found=null;
        foreach(var gap in gaps.Where(g=>g.Count>=20).OrderByDescending(g=>g.Max(i=>i/window.Columns)))
        {
            int top=gap.Max(i=>i/window.Columns);var apex=gap.Where(i=>i/window.Columns==top).ToArray();
            var candidate=new Vector2(window.Left+((float)apex.Average(i=>i%window.Columns)+.5f)*cell,bottom+(top+1)*cell);
            if(candidate.Y>hip.Y+height*.06f||Math.Abs(candidate.X-centerX)>height*.06f)continue;
            if(candidate.Y>=knee.Y+(hip.Y-knee.Y)*.55f)found=candidate;
            break;
        }
        if(found is not {} crotch)return null;
        // Thigh thickness just below the crotch, outward from it.
        // A wide stance leaves open space beside the crotch before the thigh begins.
        var start=new Vector2(crotch.X,crotch.Y-height*.015f);int steps=0,open=0,first=0,last=0;
        while(steps<limit&&!window.Inside(start+new Vector2(side*steps*cell,0)))steps++;
        first=steps;
        while(open<3&&steps<limit)
        {
            if(window.Inside(start+new Vector2(side*steps*cell,0))){open=0;last=steps;}else open++;
            steps++;
        }
        float radius=(last-first+1)*cell*.5f;
        if(radius<height*.02f||radius>height*.14f)return null;
        var axis=knee-hip;if(axis.LengthSquared()<1e-8f)return null;
        var apex3=new Vector3(crotch,window.Depth(start+new Vector2(side*radius,0))??hip.Z);
        return new(apex3,Vector3.Normalize(axis),radius,side,Between(apex3,knee,otherKnee));
    }
}
