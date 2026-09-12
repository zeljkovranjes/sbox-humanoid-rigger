namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

public sealed record HandRefinementReport(string Status,int Views,int AcceptedViews,int AdjustedJoints);

/// <summary>MediaPipe is a proposal source. Mesh depth, local centers, digit topology and cross-view agreement gate every change.</summary>
public sealed class HandPriorRefinement(IHandLandmarkModel model):IHandRefiner
{
    public HandRefinementReport Refine(ImportedCharacter character,Anatomy anatomy,string side)=>Refine(character,anatomy,side,model);
    public static HandRefinementReport Refine(ImportedCharacter character,Anatomy anatomy,string side,IHandLandmarkModel model)
    {
        // The model always predicts five digits. It cannot establish topology on a four-digit or fused hand.
        if(Profiles.Fingers.Any(f=>!anatomy.Points.ContainsKey(f+"Tip."+side)))return new("Geometry fallback: nonstandard finger topology",0,0,0);
        var views=HandView.Render(character,anatomy,side);float height=anatomy.Height;
        var observations=new List<(HandView View,HandPrediction Prediction,Vector3?[] Points)>();
        foreach(var view in views)
        {
            var prediction=model.Predict(view.Rgb);
            // Presence is a distinct model output. Handedness is never used as landmark confidence.
            if(!float.IsFinite(prediction.Presence)||prediction.Presence<.75f||prediction.Pixels.Length!=21)continue;
            var points=prediction.Pixels.Select(p=>view.TryLift(p,out var point)?(Vector3?)point:null).ToArray();
            if(points.Count(p=>p.HasValue)<18)continue;
            var expectedWrist=view.Project(anatomy["Hand."+side]);var wristPixel=prediction.Pixels[0];
            if(!Geometry.Finite(wristPixel)||MathF.Sqrt(MathF.Pow(wristPixel.X-expectedWrist.X,2)+MathF.Pow(wristPixel.Y-expectedWrist.Y,2))*view.Extent/HandView.Resolution>height*.04f)continue;
            observations.Add((view,prediction,points));
        }
        if(observations.Count<2)return new("Geometry fallback: insufficient multi-view agreement",views.Length,observations.Count,0);
        var surface=character.Meshes.Where(m=>m.Kind==MeshKind.Body).SelectMany(m=>m.Vertices).Where(p=>Vector3.Distance(p,anatomy["Hand."+side])<height*.17f).ToArray();
        var changes=new List<Landmark>();
        for(int digit=0;digit<5;digit++)
        {
            string finger=Profiles.Fingers[digit];
            var roles=Enumerable.Range(1,3).Select(j=>finger+j+"."+side).Append(finger+"Tip."+side).ToArray();
            if(roles.Any(r=>!anatomy.Points.ContainsKey(r)))continue;
            var source=roles.Select(r=>anatomy.Points[r]).ToArray();var candidate=new Vector3[4];var confidence=new float[4];bool valid=true;
            for(int joint=0;joint<4;joint++)
            {
                if(source[joint].Corrected){candidate[joint]=source[joint].Position;confidence[joint]=1;continue;}
                int index=1+digit*4+joint;
                var votes=observations.Where(o=>o.Points[index].HasValue).ToArray();
                if(votes.Length<2){valid=false;break;}
                var best=votes.OrderByDescending(o=>votes.Count(v=>Vector3.Distance(o.Points[index]!.Value,v.Points[index]!.Value)<height*.006f)).First();
                var agreed=votes.Where(v=>Vector3.Distance(best.Points[index]!.Value,v.Points[index]!.Value)<height*.006f).ToArray();
                // Front and back alone are the same projection. Require a second genuinely different viewing angle.
                if(agreed.Length<2||!agreed.Any(v=>Math.Abs(Vector3.Dot(best.View.Normal,v.View.Normal))<.95f)){valid=false;break;}
                candidate[joint]=Geometry.Mean(agreed.Select(v=>v.Points[index]!.Value));
                float spread=agreed.Average(v=>Vector3.Distance(v.Points[index]!.Value,candidate[joint]));
                confidence[joint]=agreed.Average(v=>v.Prediction.Presence)*(1-spread/(height*.012f));
            }
            if(!valid)continue;
            // Digit extrema from 3D segmentation remain authoritative; reject conflicting 2D finger identity.
            if(Vector3.Distance(candidate[3],source[3].Position)>height*.012f)continue;
            candidate[3]=source[3].Position;
            for(int joint=0;joint<3;joint++)
            {
                if(source[joint].Corrected)continue;
                var direction=candidate[joint+1]-candidate[joint];
                if(direction.LengthSquared()<1e-8f){valid=false;break;}
                candidate[joint]=BodyDetector.RefineCenter(surface,candidate[joint],direction,height*.004f);
                if(Vector3.Distance(candidate[joint],source[joint].Position)>height*.018f){valid=false;break;}
            }
            if(!valid||!Plausible(candidate,height))continue;
            // Compare both candidates against independent rendered observations and geometry support.
            float Score(Vector3[] chain)
            {
                float score=0;
                for(int j=0;j<3;j++)
                {
                    int index=1+digit*4+j;
                    var errors=observations.Where(o=>o.Points[index].HasValue).Select(o=>Vector3.Distance(chain[j],o.Points[index]!.Value)/height).Order().ToArray();
                    score+=errors.Take(2).Average();
                    var direction=chain[j+1]-chain[j];var center=BodyDetector.RefineCenter(surface,chain[j],direction,height*.004f);
                    score+=Vector3.Distance(chain[j],center)/height;
                }
                return score;
            }
            if(Score(candidate)+.001f>=Score(source.Select(p=>p.Position).ToArray()))continue;
            for(int j=0;j<3;j++)if(!source[j].Corrected&&Vector3.Distance(candidate[j],source[j].Position)>height*.0001f)
                changes.Add(new(roles[j],candidate[j],Math.Clamp(confidence[j],0,1)));
        }
        foreach(var point in changes)anatomy.Points[point.Role]=point;
        if(changes.Any(p=>p.Role.StartsWith("Thumb")))ThumbFitting.Refine(character,anatomy,side);
        if(changes.Count>0)FingerAlignment.Refine(character,anatomy,side);
        return new(changes.Count>0?"Refined with mesh-supported hand prior":"Geometry retained: no better supported candidate",views.Length,observations.Count,changes.Count);
    }
    static bool Plausible(Vector3[] chain,float height)
    {
        var lengths=Enumerable.Range(0,3).Select(i=>Vector3.Distance(chain[i],chain[i+1])).ToArray();
        if(lengths.Any(l=>l<height*.002f||l>height*.05f)||lengths.Max()>lengths.Min()*5)return false;
        for(int i=0;i<2;i++)if(Vector3.Dot(Vector3.Normalize(chain[i+1]-chain[i]),Vector3.Normalize(chain[i+2]-chain[i+1]))<.15f)return false;
        return true;
    }
}
