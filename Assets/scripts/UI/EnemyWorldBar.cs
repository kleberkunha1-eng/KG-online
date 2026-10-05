using UnityEngine;
using UnityEngine.UI;
using Mirror;

namespace TOP.UI
{
    public class EnemyWorldBar : MonoBehaviour
    {
        EnemyStats stats;
        Canvas canvas;
        RectTransform fill;
        void Start()
        {
            if(!NetworkClient.active) return;
            stats=GetComponent<EnemyStats>();
            var bar=new GameObject("MonsterHP",typeof(RectTransform),typeof(Canvas));
            bar.layer=LayerMask.NameToLayer("UI"); bar.transform.SetParent(transform,false);
            bar.transform.localPosition=new Vector3(0,1.8f,0); bar.transform.localScale=Vector3.one*.01f;
            canvas=bar.GetComponent<Canvas>(); canvas.renderMode=RenderMode.WorldSpace;
            var rect=(RectTransform)bar.transform; rect.sizeDelta=new Vector2(120,10);
            var background=new GameObject("Background",typeof(RectTransform),typeof(Image)); background.transform.SetParent(bar.transform,false);
            var backRect=(RectTransform)background.transform; backRect.anchorMin=Vector2.zero; backRect.anchorMax=Vector2.one; backRect.offsetMin=backRect.offsetMax=Vector2.zero;
            var backImage=background.GetComponent<Image>(); backImage.color=new Color(.03f,.04f,.06f,.85f); backImage.raycastTarget=false;
            var content=new GameObject("Health",typeof(RectTransform),typeof(Image)); content.transform.SetParent(background.transform,false);
            fill=(RectTransform)content.transform; fill.anchorMin=Vector2.zero; fill.anchorMax=Vector2.one; fill.offsetMin=new Vector2(1,1); fill.offsetMax=new Vector2(-1,-1);
            var image=content.GetComponent<Image>(); image.color=new Color(.35f,.9f,.25f); image.raycastTarget=false;
        }
        void LateUpdate()
        {
            if(canvas==null || stats==null) return;
            canvas.enabled=!stats.IsDead;
            fill.anchorMax=new Vector2(Mathf.Clamp01((float)stats.Health/Mathf.Max(1,stats.MaxHealth)),1);
            if(Camera.main!=null) canvas.transform.rotation=Camera.main.transform.rotation;
        }
    }
}
