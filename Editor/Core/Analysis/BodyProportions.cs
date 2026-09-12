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
        for(int sample=0;sample<=100;sample++)
        {
            float y=bottom+height*(.6f+sample*.0035f);
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
        if(narrowest.Y>=bottom+height*.8f)return height;
        int index=sections.IndexOf(narrowest),first=index,last=index;
        bool SameNeck(Section a,Section b)=>b.Area<=narrowest.Area*2.5f&&Math.Abs(a.Y-b.Y)<=height*.0071f;
        while(first>0&&SameNeck(sections[first],sections[first-1]))first--;
        while(last+1<sections.Count&&SameNeck(sections[last],sections[last+1]))last++;
        if(last-first<2)return height;
        float neckY=(sections[first].Y+sections[last].Y)*.5f;
        // Require expansion in both transverse dimensions above the neck. An arm
        // silhouette or a narrow waist alone is not sufficient evidence of a head.
        if(!sections.Any(s=>s.Y>neckY+height*.025f&&s.Width>narrowest.Width*2.2f&&s.Depth>narrowest.Depth*1.5f))return height;
        return Math.Min(height,(neckY-bottom)/.85f);
    }
}
