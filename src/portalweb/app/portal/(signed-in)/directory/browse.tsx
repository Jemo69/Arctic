"use client";
import {useCallback,useEffect,useRef,useState,type ReactNode} from "react";
import {useRouter,useSearchParams} from "next/navigation";
import {directoryRequest,teamLabels,type DirectoryCard,type DirectoryProfile,type DirectoryStatus} from "@/lib/directory";
import {HackerCard,statusLabel} from "./hacker-card";

type Page={items:DirectoryCard[];nextCursor:string|null};
// Brand marks (24px, filled) for the contact buttons; "website" is a plain globe.
const CONTACT_ICONS:Record<string,string>={
 github:"M12 .3a12 12 0 0 0-3.8 23.38c.6.12.83-.26.83-.57L9 21.07c-3.34.72-4.04-1.61-4.04-1.61-.55-1.39-1.34-1.76-1.34-1.76-1.08-.74.09-.73.09-.73 1.2.09 1.83 1.24 1.83 1.24 1.07 1.83 2.81 1.3 3.5 1 .1-.78.42-1.31.76-1.61-2.67-.3-5.47-1.33-5.47-5.93 0-1.31.47-2.38 1.24-3.22-.14-.3-.54-1.52.1-3.18 0 0 1-.32 3.3 1.23a11.5 11.5 0 0 1 6 0c2.28-1.55 3.29-1.23 3.29-1.23.64 1.66.24 2.88.12 3.18a4.65 4.65 0 0 1 1.23 3.22c0 4.61-2.8 5.63-5.48 5.92.42.36.81 1.1.81 2.22l-.01 3.29c0 .32.21.69.82.57A12 12 0 0 0 12 .3",
 linkedin:"M20.45 20.45h-3.55v-5.57c0-1.33-.03-3.04-1.85-3.04-1.85 0-2.14 1.45-2.14 2.94v5.67H9.35V9h3.41v1.56h.05c.48-.9 1.64-1.85 3.37-1.85 3.6 0 4.27 2.37 4.27 5.46v6.28zM5.34 7.43a2.06 2.06 0 1 1 0-4.13 2.06 2.06 0 0 1 0 4.13zm1.78 13.02H3.56V9h3.56v11.45zM22.22 0H1.77C.79 0 0 .77 0 1.73v20.54C0 23.23.79 24 1.77 24h20.45c.98 0 1.78-.77 1.78-1.73V1.73C24 .77 23.2 0 22.22 0z",
 discord:"M20.32 4.37a19.8 19.8 0 0 0-4.89-1.52.07.07 0 0 0-.08.04c-.21.37-.44.86-.6 1.25a18.27 18.27 0 0 0-5.49 0 12.64 12.64 0 0 0-.62-1.25.08.08 0 0 0-.08-.04 19.74 19.74 0 0 0-4.88 1.52.07.07 0 0 0-.03.03C.53 9.05-.32 13.58.1 18.06a.08.08 0 0 0 .03.05 19.9 19.9 0 0 0 5.99 3.03.08.08 0 0 0 .08-.03c.46-.63.87-1.3 1.23-1.99a.08.08 0 0 0-.04-.1 13.1 13.1 0 0 1-1.87-.9.08.08 0 0 1-.01-.12l.37-.3a.07.07 0 0 1 .08-.01c3.93 1.8 8.18 1.8 12.06 0a.07.07 0 0 1 .08.01l.37.3a.08.08 0 0 1 0 .12 12.3 12.3 0 0 1-1.88.9.08.08 0 0 0-.04.1c.36.7.77 1.36 1.23 1.99a.08.08 0 0 0 .08.03 19.84 19.84 0 0 0 6-3.03.08.08 0 0 0 .03-.05c.5-5.18-.84-9.68-3.55-13.66a.06.06 0 0 0-.03-.03zM8.02 15.33c-1.18 0-2.16-1.09-2.16-2.42s.96-2.42 2.16-2.42c1.21 0 2.18 1.1 2.16 2.42 0 1.33-.96 2.42-2.16 2.42zm7.97 0c-1.18 0-2.15-1.09-2.15-2.42s.95-2.42 2.15-2.42c1.21 0 2.18 1.1 2.16 2.42 0 1.33-.95 2.42-2.16 2.42z",
 website:"M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm6.93 6h-2.95a15.6 15.6 0 0 0-1.38-3.56A8.03 8.03 0 0 1 18.93 8zM12 4.04c.83 1.2 1.48 2.53 1.91 3.96h-3.82c.43-1.43 1.08-2.76 1.91-3.96zM4.26 14a8.2 8.2 0 0 1 0-4h3.38a16.5 16.5 0 0 0 0 4H4.26zm.81 2h2.95c.32 1.25.78 2.45 1.38 3.56A7.99 7.99 0 0 1 5.07 16zm2.95-8H5.07a7.99 7.99 0 0 1 4.33-3.56A15.6 15.6 0 0 0 8.02 8zM12 19.96A14.1 14.1 0 0 1 10.09 16h3.82A14.1 14.1 0 0 1 12 19.96zM14.34 14H9.66a14.7 14.7 0 0 1 0-4h4.68a14.7 14.7 0 0 1 0 4zm.25 5.56c.6-1.11 1.06-2.31 1.38-3.56h2.95a8.03 8.03 0 0 1-4.33 3.56zM16.36 14a16.5 16.5 0 0 0 0-4h3.38a8.2 8.2 0 0 1 0 4h-3.38z",
};
/** One filter as a pill that opens a small list. Single-choice menus close on pick; multi-choice ones stay open. */
export function FilterMenu({label,options,selected,multi=false,searchable=false,name=(v:string)=>v,onChange}:{label:string;options:string[];selected:string[];multi?:boolean;searchable?:boolean;name?:(v:string)=>string;onChange:(next:string[])=>void}){
 const [open,setOpen]=useState(false);const [find,setFind]=useState("");const box=useRef<HTMLDivElement>(null);
 useEffect(()=>{if(!open)return;const away=(e:MouseEvent)=>{if(!box.current?.contains(e.target as Node))setOpen(false)};const esc=(e:KeyboardEvent)=>{if(e.key==="Escape")setOpen(false)};document.addEventListener("mousedown",away);document.addEventListener("keydown",esc);return()=>{document.removeEventListener("mousedown",away);document.removeEventListener("keydown",esc)}},[open]);
 const shown=options.filter(o=>!find||name(o).toLowerCase().includes(find.toLowerCase()));
 const summary=selected.length?`${name(selected[0])}${selected.length>1?` +${selected.length-1}`:""}`:"Any";
 return <div className="filter-menu" ref={box}><button type="button" className="filter-chip" aria-expanded={open} data-active={selected.length?"":undefined} onClick={()=>setOpen(o=>!o)}>{label}<span>{summary}</span><svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" aria-hidden="true"><path d="M6 9l6 6 6-6"/></svg></button>
 {open?<div className="filter-pop" role="group" aria-label={label}>{searchable?<input type="search" className="filter-find" placeholder={`Find ${label.toLowerCase()}`} aria-label={`Find ${label.toLowerCase()}`} value={find} onChange={e=>setFind(e.target.value)} autoFocus/>:null}
 <div className="filter-options">{shown.map(o=>{const on=selected.includes(o);return <label key={o} className="filter-option"><input type="checkbox" checked={on} onChange={()=>{onChange(multi?(on?selected.filter(x=>x!==o):[...selected,o]):(on?[]:[o]));if(!multi)setOpen(false)}}/>{name(o)}</label>})}{shown.length===0?<p className="quiet">No match.</p>:null}</div>
 {selected.length?<button type="button" className="link" onClick={()=>onChange([])}>Reset {label.toLowerCase()}</button>:null}</div>:null}</div>
}

export function BrowseDirectory({status,self,selfId}:{status:DirectoryStatus;self?:ReactNode;selfId?:string}){
 const params=useSearchParams();const router=useRouter();const [query,setQuery]=useState(params.get("q")??"");
 const [page,setPage]=useState<Page>({items:[],nextCursor:null});const [busy,setBusy]=useState(true);const [more,setMore]=useState(false);const [error,setError]=useState("");const [reload,setReload]=useState(0);
 const [detail,setDetail]=useState<DirectoryProfile|null>(null);const [detailError,setDetailError]=useState("");const [copied,setCopied]=useState("");const dialog=useRef<HTMLDialogElement>(null);const opener=useRef<HTMLButtonElement|null>(null);
 const all=params.toString();const filters=new URLSearchParams(all);filters.delete("h");filters.delete("edit");const filterKey=filters.toString();const selected=params.get("h");
 const update=useCallback((key:string,value:string)=>{const p=new URLSearchParams(window.location.search);value?p.set(key,value):p.delete(key);p.delete("h");router.replace(`/portal/directory${p.size?`?${p}`:""}`,{scroll:false})},[router]);
 useEffect(()=>setQuery(params.get("q")??""),[params]);
 const search=useRef<HTMLInputElement>(null);
 // "/" jumps to search from anywhere that is not already a text field.
 useEffect(()=>{const key=(e:KeyboardEvent)=>{const t=e.target as HTMLElement;if(e.key==="/"&&!e.metaKey&&!e.ctrlKey&&!/^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName)&&!t.isContentEditable){e.preventDefault();search.current?.focus()}};document.addEventListener("keydown",key);return()=>document.removeEventListener("keydown",key)},[]);
 useEffect(()=>{if(query===(params.get("q")??""))return;const timer=setTimeout(()=>update("q",query.trim()),300);return()=>clearTimeout(timer)},[query,params,update]);
 useEffect(()=>{const controller=new AbortController();setBusy(true);setError("");setPage({items:[],nextCursor:null});
 directoryRequest<Page>(`?${filterKey}`,{signal:controller.signal}).then(setPage).catch(e=>{if(e.name!=="AbortError")setError(e.message)}).finally(()=>{if(!controller.signal.aborted)setBusy(false)});return()=>controller.abort()},[filterKey,reload]);
 useEffect(()=>{if(!selected){dialog.current?.close();opener.current?.focus();setDetail(null);return}const controller=new AbortController();setDetail(null);setDetailError("");setCopied("");dialog.current?.showModal();
 directoryRequest<DirectoryProfile>(`/${encodeURIComponent(selected)}`,{signal:controller.signal}).then(setDetail).catch(e=>{if(e.name!=="AbortError")setDetailError(e.message)});return()=>controller.abort()},[selected]);
 function close(){const p=new URLSearchParams(window.location.search);p.delete("h");router.replace(`/portal/directory${p.size?`?${p}`:""}`,{scroll:false})}
 async function loadMore(){
  if(!page.nextCursor)return;
  const key=filterKey;
  // A late page or error from filters the viewer has since changed is dropped (#244); h and edit are not filters.
  const isCurrentFilter=()=>{const current=new URLSearchParams(window.location.search);current.delete("h");current.delete("edit");return current.toString()===key};
  setMore(true);setError("");
  try{
   const result=await directoryRequest<Page>(`?${key}&cursor=${encodeURIComponent(page.nextCursor)}`);
   if(isCurrentFilter())setPage(p=>({items:[...p.items,...result.items.filter(i=>!p.items.some(old=>old.id===i.id))],nextCursor:result.nextCursor}));
  }catch(e){if(isCurrentFilter())setError((e as Error).message)}finally{setMore(false)}
 }
 const skills=(params.get("skills")??"").split(",").filter(Boolean);const one=(k:string)=>params.get(k)?[params.get(k)!]:[];
 return <><div className="directory-bar"><label className="directory-search"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" aria-hidden="true"><circle cx="11" cy="11" r="7"/><path d="M20 20l-3.5-3.5"/></svg><span className="visually-hidden">Search hackers</span><input ref={search} type="search" value={query} maxLength={100} placeholder="Search names, schools, skills" onChange={e=>setQuery(e.target.value)}/><kbd aria-hidden="true">/</kbd></label>
 <div className="directory-chips" role="group" aria-label="Filters">
 <FilterMenu label="Team" options={status.catalogues.teamStatuses} selected={one("status")} name={v=>teamLabels[v]??v} onChange={v=>update("status",v[0]??"")}/>
 <FilterMenu label="Skills" options={status.catalogues.skills} selected={skills} multi searchable onChange={v=>update("skills",v.join(","))}/>
 <FilterMenu label="Pace" options={status.catalogues.paces} selected={one("pace")} onChange={v=>update("pace",v[0]??"")}/>
 <button type="button" className="filter-chip" aria-pressed={params.get("firstTimer")==="true"} data-active={params.get("firstTimer")==="true"?"":undefined} onClick={()=>update("firstTimer",params.get("firstTimer")==="true"?"":"true")}>First hackathon</button>
 {filterKey?<button type="button" className="link" onClick={()=>{setQuery("");router.replace("/portal/directory",{scroll:false})}}>Clear all</button>:null}
 <span className="directory-count" aria-live="polite">{busy?"Searching…":`${page.items.length}${page.nextCursor?"+":""} on file`}</span></div>
</div>
 {error?<div role="alert"><p>{error}</p><button onClick={()=>setReload(v=>v+1)}>Try again</button></div>:null}
 {busy?<div role="status"><p>Loading hackers…</p><div className="directory-grid">{[1,2,3].map(i=><div key={i} className="panel directory-skeleton"/>)}</div></div>:<>
 {!error&&page.items.length===0?<p>No hackers match these filters. Try clearing the filters.</p>:null}
 {page.items.length>0&&page.items.length<10&&!page.nextCursor&&!filterKey?<p className="quiet">More hackers join as RSVPs come in.</p>:null}
 <div className="directory-grid">{self}{page.items.filter(p=>p.id!==selfId).map(p=><button key={p.id} className="directory-card-button" aria-label={`View ${p.displayName}'s profile`} onClick={e=>{opener.current=e.currentTarget;const q=new URLSearchParams(all);q.set("h",p.id!);router.push(`/portal/directory?${q}`,{scroll:false})}}><HackerCard profile={p}/></button>)}</div>
 {page.nextCursor?<button disabled={more} onClick={()=>void loadMore()}>{more?"Loading…":"Load more"}</button>:null}</>}
 <dialog className="directory-sheet" ref={dialog} aria-label="Hacker profile" onCancel={e=>{e.preventDefault();close()}}>
 <header className="sheet-head"><span className="sheet-kicker">Profile</span><button type="button" className="profile-modal__close" autoFocus aria-label="Close profile" onClick={close}><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18"/></svg></button></header>
 {detailError?<p role="alert">{detailError}</p>:detail?<article className="sheet" data-status={detail.teamStatus??undefined}>
 <span className="sheet-photo">{detail.photoUrl?<img src={detail.photoUrl} width={300} height={300} alt={`Photo of ${detail.displayName??"hacker"}`}/>:<span className="hacker-avatar" aria-hidden="true">{detail.displayName?.slice(0,1)??"?"}</span>}</span>
 <h2>{detail.displayName}</h2><p className="sheet-meta">{[detail.school,detail.yearLevel,detail.pronouns].filter(Boolean).join(" · ")}</p>
 <p className="hacker-card__status">{statusLabel(detail)}</p>
{detail.contactLinks.length?<><div className="sheet-contacts" role="group" aria-label="Get in touch">{detail.contactLinks.map(l=>{const label={discord:"Discord",github:"GitHub",linkedin:"LinkedIn",website:"Website"}[l.kind]??l.kind;const icon=<svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d={CONTACT_ICONS[l.kind]??CONTACT_ICONS.website}/></svg>;
 return l.kind==="discord"?<button key={l.kind} type="button" aria-label={`Copy Discord username ${l.value}`} title={`Discord: ${l.value} (click to copy)`} onClick={async()=>{try{await navigator.clipboard.writeText(l.value);setCopied(`Copied Discord username ${l.value}.`)}catch{setCopied(`Discord: ${l.value}`)}}}>{icon}</button>
 :<a key={l.kind} className="button" href={l.kind==="github"?`https://github.com/${l.value}`:l.value} target="_blank" rel="noopener noreferrer" aria-label={`${label} (opens in a new tab)`} title={label}>{icon}</a>})}</div><p role="status">{copied}</p></>:null}
{detail.headline?<p className="sheet-headline">{detail.headline}</p>:null}
 {/* Only what they filled in: a sheet of "Not specified" reads as empty. */}
 <dl className="sheet-facts">{detail.buildIdea?<div><dt>Wants to build</dt><dd>{detail.buildIdea}</dd></div>:null}{detail.lookingFor?<div><dt>Looking for</dt><dd>{detail.lookingFor}</dd></div>:null}
 {detail.skills.length?<div><dt>Skills</dt><dd><ul className="directory-tags">{detail.skills.map(s=><li key={s}>{s}</li>)}</ul></dd></div>:null}
 {detail.interests.length?<div><dt>Interests</dt><dd><ul className="directory-tags">{detail.interests.map(s=><li key={s}>{s}</li>)}</ul></dd></div>:null}
 {detail.pace?<div><dt>Pace</dt><dd>{detail.pace}</dd></div>:null}{detail.firstHackathon!==null?<div><dt>First hackathon</dt><dd>{detail.firstHackathon?"Yes":"No"}</dd></div>:null}</dl>
 </article>:<p role="status">Loading profile…</p>}
 </dialog></>
}
