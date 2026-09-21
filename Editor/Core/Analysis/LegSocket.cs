#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;
using Vector2=System.Numerics.Vector2;

/// <summary>Measures where a leg leaves the pelvis. The crotch is the top of
/// the open space between the legs in the frontal silhouette. From there the
/// thigh's boundary rises toward the side of the hip, as the groin crease and
/// the buttock do: a level cut through the hip joint instead leaves the whole
/// upper thigh on the pelvis and hinges the leg at the crotch.</summary>
internal sealed record LegSocket(Vector3 Crotch,Vector3 Axis,float Radius,float Side)
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
        return TrunkRegion.Smooth(depth/Radius+.5f)*crest;
    }
    internal static LegSocket? Measure(IEnumerable<MeshPart> meshes,Vector3 hip,Vector3 knee,float centerX,float height)
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
        return new(new(crotch,window.Depth(start+new Vector2(side*radius,0))??hip.Z),Vector3.Normalize(axis),radius,side);
    }
}
