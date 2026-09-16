namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>A low, narrow neck beneath an enlarged head provides a body-scale
/// prior independent of total height. Ambiguous sections retain the original prior.</summary>
internal static class BodyProportions
{
    readonly record struct Section(float Y,float Width,float Depth)
    {
        public float Area=>Width*Depth;
    }
    public static float EstimateBodyHeight(IEnumerable<MeshPart> source,float bottom,float height)
    {
        var meshes=source.ToArray();var sections=new List<Section>();
        // Intersect triangle edges rather than sampling vertices: dense faces,
        // sparse neck rings and material seams should give the same cross-section.
        for(int sample=0;sample<=128;sample++)
        {
            float y=bottom+height*(.5f+sample*.0035f);
            float minX=float.PositiveInfinity,maxX=float.NegativeInfinity,minZ=float.PositiveInfinity,maxZ=float.NegativeInfinity;
            int count=0;
            foreach(var mesh in meshes)for(int t=0;t<mesh.Triangles.Length;t+=3)for(int edge=0;edge<3;edge++)
            {
                var a=mesh.Vertices[mesh.Triangles[t+edge]];var b=mesh.Vertices[mesh.Triangles[t+(edge+1)%3]];
                if(!((a.Y<=y&&b.Y>y)||(b.Y<=y&&a.Y>y)))continue;
                var p=Vector3.Lerp(a,b,(y-a.Y)/(b.Y-a.Y));
                minX=Math.Min(minX,p.X);maxX=Math.Max(maxX,p.X);minZ=Math.Min(minZ,p.Z);maxZ=Math.Max(maxZ,p.Z);count++;
            }
            if(count>=4&&maxX-minX>height*.005f&&maxZ-minZ>height*.005f)sections.Add(new(y,maxX-minX,maxZ-minZ));
        }
        var candidates=sections.Where(s=>s.Y<bottom+height*.9f).ToArray();
        if(candidates.Length==0)return height;
        var narrowest=candidates.MinBy(s=>s.Area);
        if(narrowest.Y>=bottom+height*.8f)return LocalNeckHeight();
        int index=sections.IndexOf(narrowest),first=index,last=index;
        bool SameNeck(Section a,Section b)=>b.Area<=narrowest.Area*2.5f&&Math.Abs(a.Y-b.Y)<=height*.0071f;
        while(first>0&&SameNeck(sections[first],sections[first-1]))first--;
        while(last+1<sections.Count&&SameNeck(sections[last],sections[last+1]))last++;
        if(last-first<2)return LocalNeckHeight();
        float neckY=(sections[first].Y+sections[last].Y)*.5f;
        // An unusually narrow waist is not the neck if another bottleneck
        // separates the shoulders and skull farther up the same silhouette.
        foreach(var later in sections.Where(s=>s.Y>neckY+height*.1f&&s.Y<bottom+height*.9f))
        {
            bool Expanded(Section s)=>s.Width>later.Width*1.35f&&s.Depth>later.Depth*1.1f;
            if(sections.Any(s=>s.Y<later.Y-height*.02f&&s.Y>later.Y-height*.07f&&Expanded(s))&&
                sections.Any(s=>s.Y>later.Y+height*.02f&&s.Y<later.Y+height*.07f&&Expanded(s)))return LocalNeckHeight();
        }
        // Require expansion in both transverse dimensions above the neck. An arm
        // silhouette or a narrow waist alone is not sufficient evidence of a head.
        if(!sections.Any(s=>s.Y>Math.Max(neckY+height*.025f,bottom+height*.82f)&&s.Width>narrowest.Width*2.2f&&s.Depth>narrowest.Depth*1.5f))return LocalNeckHeight();
        return Math.Min(height,(neckY-bottom)/.85f);

        float LocalNeckHeight()
        {
            // Ears, horns and crests can be narrower than the actual neck and
            // extend above the skull. Require a local bottleneck with shoulder
            // and head expansion on either side, independent of the crown.
            var necks=candidates.Where(s=>sections.Where(n=>Math.Abs(n.Y-s.Y)<height*.014f).All(n=>n.Area>=s.Area))
                .Where(s=>sections.Any(n=>n.Y<s.Y-height*.02f&&n.Y>s.Y-height*.10f&&n.Width>s.Width*1.75f&&n.Depth>s.Depth*1.1f))
                .Where(s=>sections.Any(n=>n.Y>s.Y+height*.02f&&n.Y<s.Y+height*.12f&&n.Width>s.Width*1.75f&&n.Depth>s.Depth*1.5f))
                .OrderByDescending(s=>s.Y).ToArray();
            if(necks.Length==0||necks[0].Y>=bottom+height*.8f)return height;
            var neck=necks[0];
            var character=new ImportedCharacter{Meshes=meshes};
            var vertices=meshes.SelectMany(m=>m.Vertices).ToArray();
            float center=HumanoidCenterline.Estimate(vertices,bottom,height);
            var contour=MeshSections.Cut(character,new(center,neck.Y,0),Vector3.UnitY,height*.3f,height*1e-5f)
                .Where(s=>Math.Abs(s.Center.X-center)<height*.025f&&s.Area>height*height*.0005f)
                .OrderByDescending(s=>s.Area).FirstOrDefault();
            if(contour is null)return height;
            return Math.Min(height,(neck.Y-bottom)/.85f);
        }
    }
}
