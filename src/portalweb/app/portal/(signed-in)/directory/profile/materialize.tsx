"use client";
import {useEffect,useRef,useState,type ReactNode} from "react";

const GLYPHS="ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789#$%&*+<>/=";
const COLUMN=12,ROW=15,DURATION=1400,BAND=110;
const ease=(t:number)=>t<.5?4*t*t*t:1-Math.pow(-2*t+2,3)/2;

/**
 * Builds its children out of glyphs. One eased wave runs down the box: ahead of it glyphs
 * flicker, at the front they flare, behind it they fade as the real content is unmasked.
 * The same progress is written as --form for the glass and the backdrop blur, so all of it moves together.
 */
export function Materialize({className,children}:{className?:string;children:ReactNode}){
 const box=useRef<HTMLDivElement>(null);const body=useRef<HTMLDivElement>(null);const canvas=useRef<HTMLCanvasElement>(null);const [formed,setFormed]=useState(false);
 useEffect(()=>{const el=box.current,inner=body.current,c=canvas.current,ctx=c?.getContext("2d");if(!el||!inner||!c||!ctx)return;
 // On the dialog when there is one: its ::backdrop inherits from it, so the page behind blurs in step.
 const host=el.closest("dialog")??el;
 const step=(form:number,front:number)=>{host.style.setProperty("--form",String(form));inner.style.setProperty("--front",`${front-inner.offsetTop}px`)};
 if(matchMedia("(prefers-reduced-motion: reduce)").matches){step(1,1e5);setFormed(true);return}
 const rect=el.getBoundingClientRect(),dpr=devicePixelRatio||1;
 // Only the part on screen gets glyphs, and the wave paces itself to it; the rest unmasks when it lands.
 const w=rect.width,h=Math.max(0,Math.min(rect.height,innerHeight-rect.top));c.width=w*dpr;c.height=h*dpr;c.style.height=`${h}px`;ctx.scale(dpr,dpr);
 const style=getComputedStyle(el);ctx.font=`${ROW}px ${style.getPropertyValue("--font-courier")||"monospace"}, monospace`;ctx.textBaseline="top";
 const cols=Math.ceil(w/COLUMN),rows=Math.ceil(h/ROW),n=cols*rows;
 const jitter=Float32Array.from({length:n},()=>(Math.random()-.5)*BAND*.8);const glyph=Uint8Array.from({length:n},()=>Math.random()*GLYPHS.length);
 const travel=h+BAND*2;let frame=0;const start=performance.now();
 const draw=(now:number)=>{const t=Math.min(1,(now-start)/DURATION),e=ease(t),front=e*travel-BAND;step(e,front);ctx.clearRect(0,0,w,h);
 const fadeIn=Math.min(1,t*8);
 for(let i=0;i<n;i++){const d=Math.floor(i/cols)*ROW+jitter[i]-front;if(d<-BAND)continue;
 const near=Math.max(0,1-Math.abs(d)/BAND);ctx.globalAlpha=(d>=0?1:1+d/BAND)*(.28+.72*near*near)*fadeIn;ctx.fillStyle=near>.8?"#fff":style.color;
 if(Math.random()<.06)glyph[i]=(Math.random()*GLYPHS.length)|0;ctx.fillText(GLYPHS[glyph[i]],(i%cols)*COLUMN,Math.floor(i/cols)*ROW)}
 if(t<1)frame=requestAnimationFrame(draw);else{ctx.clearRect(0,0,w,h);step(1,1e5);setFormed(true)}};
 frame=requestAnimationFrame(draw);return()=>{cancelAnimationFrame(frame);host.style.removeProperty("--form")}},[]);
 return <div ref={box} className={`materialize ${className??""}`} data-formed={formed?"":undefined}><canvas ref={canvas} className="materialize__glyphs" aria-hidden="true" hidden={formed}/><div ref={body} className="materialize__body">{children}</div></div>
}
