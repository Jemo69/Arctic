"use client";
import {useEffect,useLayoutEffect,useRef,useState} from "react";
import {OUT,VIEW,clamp,cropRect} from "./photo-crop-math";


/**
 * Square crop before upload: drag (or arrow keys) to move, slider to zoom. The result is already square,
 * so the server's centre crop to 400px keeps all of it.
 */
export function PhotoCrop({file,onDone,onCancel}:{file:File;onDone:(cropped:File)=>void;onCancel:()=>void}){
 const dialog=useRef<HTMLDialogElement>(null);const img=useRef<HTMLImageElement>(null);const drag=useRef<{x:number;y:number}|null>(null);
 const [url,setUrl]=useState("");const [size,setSize]=useState<{w:number;h:number}|null>(null);
 const [zoom,setZoom]=useState(1);const [pos,setPos]=useState({x:0,y:0});const [error,setError]=useState("");
 useLayoutEffect(()=>{dialog.current?.showModal()},[]);
 // Made in the effect, not the initial state, so a remount never reuses a revoked URL.
 useEffect(()=>{const u=URL.createObjectURL(file);setUrl(u);return()=>URL.revokeObjectURL(u)},[file]);
 const scale=size?VIEW/Math.min(size.w,size.h)*zoom:1;const w=(size?.w??0)*scale,h=(size?.h??0)*scale;
 function load(){const i=img.current!;if(i.naturalWidth<200||i.naturalHeight<200){setError("Choose a photo at least 200 pixels on each side.");return}
  const s=VIEW/Math.min(i.naturalWidth,i.naturalHeight);setSize({w:i.naturalWidth,h:i.naturalHeight});setPos({x:(VIEW-i.naturalWidth*s)/2,y:(VIEW-i.naturalHeight*s)/2})}
 // Zoom around the centre of the square, so what you are looking at stays put.
 function rezoom(next:number){if(!size)return;const s0=VIEW/Math.min(size.w,size.h),old=s0*zoom,s=s0*next;
  const cx=(VIEW/2-pos.x)/old,cy=(VIEW/2-pos.y)/old;setZoom(next);setPos(clamp(VIEW/2-cx*s,VIEW/2-cy*s,size.w*s,size.h*s))}
 const move=(dx:number,dy:number)=>setPos(p=>clamp(p.x+dx,p.y+dy,w,h));
 function apply(){const c=document.createElement("canvas");c.width=c.height=OUT;const ctx=c.getContext("2d");if(!ctx||!img.current)return;
  ctx.imageSmoothingQuality="high";const {sx,sy,size}=cropRect(pos,scale);ctx.drawImage(img.current,sx,sy,size,size,0,0,OUT,OUT);
  c.toBlob(b=>b?onDone(new File([b],"photo.jpg",{type:"image/jpeg"})):setError("Couldn’t read that photo. Try another."),"image/jpeg",.9)}
 return <dialog ref={dialog} className="photo-crop" aria-labelledby="photo-crop-title" onCancel={e=>{e.preventDefault();onCancel()}}>
  <h2 id="photo-crop-title">Position your photo</h2><p className="quiet">Drag to move. Use the slider to zoom.</p>
  <div className="photo-crop__view" tabIndex={0} role="application" aria-label="Photo position. Arrow keys move it." style={{width:VIEW,height:VIEW}}
   onKeyDown={e=>{const d={ArrowLeft:[10,0],ArrowRight:[-10,0],ArrowUp:[0,10],ArrowDown:[0,-10]}[e.key];if(d){e.preventDefault();move(d[0],d[1])}}}
   onPointerDown={e=>{e.currentTarget.setPointerCapture(e.pointerId);drag.current={x:e.clientX,y:e.clientY}}}
   onPointerMove={e=>{if(!drag.current)return;move(e.clientX-drag.current.x,e.clientY-drag.current.y);drag.current={x:e.clientX,y:e.clientY}}}
   onPointerUp={()=>{drag.current=null}} onPointerCancel={()=>{drag.current=null}}>
   {url?<img ref={img} src={url} alt="" draggable={false} onLoad={load} onError={()=>setError("Couldn’t read that photo. Try a JPEG, PNG or WebP.")} style={{width:w||undefined,height:h||undefined,transform:`translate(${pos.x}px,${pos.y}px)`,visibility:size?"visible":"hidden"}}/>:null}
  </div>
  <label className="photo-crop__zoom"><span>Zoom</span><input type="range" min={1} max={3} step={.01} value={zoom} disabled={!size} onChange={e=>rezoom(Number(e.target.value))}/></label>
  {error?<p role="alert" className="directory-error">{error}</p>:null}
  <div className="photo-crop__actions"><button type="button" onClick={onCancel}>Cancel</button><button type="button" className="primary" disabled={!size} onClick={apply}>Use photo</button></div>
 </dialog>
}
