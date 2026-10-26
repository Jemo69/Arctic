"use client";
import {useEffect,useState} from "react";
import {directoryRequest,teamLabels,type DirectoryProfile,type DirectoryStatus,type OwnProfile} from "@/lib/directory";
import {TeammateField} from "./teammate-field";
import {PhotoCrop} from "./photo-crop";
import {HackerCard} from "../hacker-card";
import {FilterMenu} from "../browse";

export function ProfileEditor({status,initial,onDirty}:{status:DirectoryStatus;initial:OwnProfile;onDirty?:(dirty:boolean)=>void}){
 const [own,setOwn]=useState(initial);const [profile,setProfile]=useState(initial.profile);const [crop,setCrop]=useState<File|null>(null);
 const [busy,setBusy]=useState(false);const [error,setError]=useState("");const [message,setMessage]=useState("");const [errors,setErrors]=useState<Record<string,string[]>>({});
 const dirty=JSON.stringify(profile)!==JSON.stringify(own.profile);const archived=status.phase==="archived";
 useEffect(()=>onDirty?.(dirty),[dirty,onDirty]);
 useEffect(()=>{if(!dirty)return;const unload=(e:BeforeUnloadEvent)=>{e.preventDefault();e.returnValue=""};
 const leave=(e:MouseEvent)=>{const a=(e.target as Element).closest("a");if(a&&a.href!==location.href&&!window.confirm("Leave without saving your profile?"))e.preventDefault()};
 window.addEventListener("beforeunload",unload);document.addEventListener("click",leave,true);return()=>{window.removeEventListener("beforeunload",unload);document.removeEventListener("click",leave,true)}},[dirty]);
 async function action(path:string,init:RequestInit={method:"POST"}){setBusy(true);setError("");setErrors({});setMessage("");try{const result=await directoryRequest<OwnProfile>(path,init);setOwn(result);setProfile(result.profile);setMessage("Saved.")}catch(e){setError((e as Error).message);setErrors((e as {fields?:Record<string,string[]>}).fields??{})}finally{setBusy(false)}}
 function set<K extends keyof DirectoryProfile>(key:K,value:DirectoryProfile[K]){if(profile)setProfile({...profile,[key]:value})}
 // The cropper hands back a 600px square JPEG, well under the 5 MB limit whatever was picked.
 async function photo(file:File){setCrop(null);setBusy(true);setError("");try{const form=new FormData();form.set("file",file);const result=await directoryRequest<{photoId:string;previewUrl:string}>("/profile/photo",{method:"POST",body:form});setProfile(p=>p?{...p,photoId:result.photoId,photoUrl:result.previewUrl}:p)}catch(e){setError((e as Error).message)}finally{setBusy(false)}}
 // Counters appear only near the limit; the rest of the time they are noise.
 const count=(id:string,length:number,max:number)=>length>max*.8?<small id={`${id}-count`} className="editor-count">{length} / {max}</small>:null;
 function field(key:"displayName"|"school"|"pronouns"|"headline"|"buildIdea",label:string,max:number,placeholder?:string){const value=profile?.[key]??"";return <div className="directory-field" key={key}><label htmlFor={key}>{label}</label><input id={key} value={value} maxLength={max} placeholder={placeholder} onChange={e=>set(key,e.target.value)} aria-invalid={!!errors[key]} aria-describedby={`${key}-count ${key}-error`} />{count(key,value.length,max)}<span id={`${key}-error`} className="directory-error">{errors[key]?.join(" ")}</span></div>}
 const missing=own.missingFields.map(s=>({displayName:"display name",school:"school",skills:"skills",contactLinks:"contact links"}[s]??s));
 return <>
 {archived?<p className="notice">The directory has closed. Your profile is read-only.</p>:status.phase==="notOpen"?<p className="notice">{status.opensAt?`Your complete, opted-in profile appears on ${new Date(status.opensAt).toLocaleDateString("en-US",{timeZone:"America/New_York"})}.`:"The opening date has not been announced. You can prepare your profile now."}</p>:null}
 {own.state==="hidden"?<p className="notice">Organizers have hidden your profile: {own.hiddenReason}. You can still edit it.</p>:null}
 {own.photoRemoved?<p className="notice">Organizers removed your photo. You can upload another.</p>:null}
 {!profile?<><p role="status">{message}</p>{error?<p role="alert" className="directory-error">{error}</p>:null}<button disabled={busy||archived} onClick={()=>action("/profile/opt-in")}>Join the hacker directory</button></>:
 <form className="directory-editor" onSubmit={e=>{e.preventDefault();void action("/profile",{method:"PUT",body:JSON.stringify(profile)})}}><fieldset disabled={busy||archived} className="editor-body">
 <aside className="editor-rail">
  <div className="editor-preview"><HackerCard profile={profile}/>
   <label className="editor-photo-button"><input type="file" className="visually-hidden" accept="image/jpeg,image/png,image/webp" onChange={e=>{const f=e.target.files?.[0];e.target.value="";if(f)setCrop(f)}}/>{profile.photoId?"Change photo":"Add photo"}</label></div>
  <p className="quiet editor-hint">{profile.photoId?<button type="button" className="link" onClick={()=>setProfile({...profile,photoId:null,photoUrl:null})}>Remove photo</button>:"JPEG, PNG or WebP. You can crop it next."}</p>
  <label className="directory-optin"><input type="checkbox" checked={own.optedIn} disabled={busy||archived} onChange={e=>{if(!dirty||window.confirm("Discard unsaved edits and change visibility?"))void action(e.target.checked?"/profile/opt-in":"/profile/opt-out")}}/>Show me in the directory</label>
  <p className="quiet editor-hint">Only RSVP’d hackers see your full profile. {own.state==="live"&&status.phase==="open"?"Your saved profile is live.":"This preview is only visible to you."}</p>
 </aside>
 <section className="editor-col" aria-label="About you">
  {field("displayName","Display name",200)}{field("school","School",200)}
  <div className="editor-row"><div className="directory-field"><label htmlFor="yearLevel">Class</label>
   <select id="yearLevel" value={status.catalogues.classifications.includes(profile.yearLevel??"")?profile.yearLevel??"":""} onChange={e=>set("yearLevel",e.target.value||null)} aria-invalid={!!errors.yearLevel} aria-describedby="yearLevel-help yearLevel-error">
   <option value="">Select</option>{status.catalogues.classifications.map(value=><option key={value} value={value}>{value}</option>)}</select>
   {profile.yearLevel&&!status.catalogues.classifications.includes(profile.yearLevel)?<small id="yearLevel-help">Choose one to replace your saved “{profile.yearLevel}”.</small>:null}
   <span id="yearLevel-error" className="directory-error">{errors.yearLevel?.join(" ")}</span></div>{field("pronouns","Pronouns",30,"she/her")}</div>
  {field("headline","Headline",160,"Building ML tools for farmers")}
  {field("buildIdea","What I want to build",280,"An app that…")}
  <fieldset className="editor-group"><legend>Team status</legend><div className="choice-chips">{status.catalogues.teamStatuses.map(v=><label key={v}><input type="radio" name="teamStatus" value={v} checked={profile.teamStatus===v} onChange={()=>setProfile({...profile,teamStatus:v,teammatesNeeded:v==="needMembers"?1:null})}/>{teamLabels[v]}</label>)}</div></fieldset>
  <div className="editor-row">{profile.teamStatus==="needMembers"?<div className="directory-field"><label htmlFor="teammatesNeeded">Teammates needed</label><select id="teammatesNeeded" value={profile.teammatesNeeded??1} onChange={e=>set("teammatesNeeded",Number(e.target.value))}>{Array.from({length:status.catalogues.maxTeamSize-1},(_,i)=><option key={i} value={i+1}>{i+1}</option>)}</select><span className="directory-error">{errors.teammatesNeeded?.join(" ")}</span></div>:null}
   <div className="directory-field"><label htmlFor="pace">Weekend pace</label><select id="pace" value={profile.pace??""} onChange={e=>set("pace",e.target.value||null)}><option value="">Not specified</option>{status.catalogues.paces.map(v=><option key={v}>{v}</option>)}</select><span className="directory-error">{errors.pace?.join(" ")}</span></div>
   <div className="directory-field"><label htmlFor="firstHackathon">First hackathon</label><select id="firstHackathon" value={profile.firstHackathon===null?"":String(profile.firstHackathon)} onChange={e=>set("firstHackathon",e.target.value===""?null:e.target.value==="true")}><option value="">Not specified</option><option value="true">Yes</option><option value="false">No</option></select></div></div>
 </section>
 <section className="editor-col" aria-label="Teaming">
  <fieldset className="editor-group" aria-describedby="skills-error"><legend>Skills <span className="quiet">up to 5</span></legend>
   <div className="editor-tags">{profile.skills.map(s=><button key={s} type="button" className="editor-tag" aria-label={`Remove ${s}`} onClick={()=>set("skills",profile.skills.filter(v=>v!==s))}>{s}<span aria-hidden="true">×</span></button>)}
   {profile.skills.length<5?<FilterMenu label="Add skill" options={status.catalogues.skills} selected={profile.skills} multi searchable onChange={v=>set("skills",v.slice(0,5))}/>:null}</div>
   <span id="skills-error" className="directory-error">{errors.skills?.join(" ")}</span></fieldset>
  <fieldset className="editor-group" aria-describedby="interests-error"><legend>Interests or tracks</legend><div className="choice-chips">{status.catalogues.interests.map(v=>{const on=profile.interests.includes(v);return <label key={v}><input type="checkbox" checked={on} disabled={!on&&profile.interests.length>=5} onChange={()=>set("interests",on?profile.interests.filter(s=>s!==v):[...profile.interests,v])}/>{v}</label>})}</div><span id="interests-error" className="directory-error">{errors.interests?.join(" ")}</span></fieldset>
  <TeammateField value={profile.lookingFor??""} errors={errors.lookingFor} onChange={value=>set("lookingFor",value)}/>
  <fieldset className="editor-group" aria-describedby="contactLinks-error"><legend>Contact links <span className="quiet">up to 3</span></legend>
   {profile.contactLinks.map((link,i)=><div className="editor-contact" key={i}><select aria-label={`Contact ${i+1} kind`} value={link.kind} onChange={e=>set("contactLinks",profile.contactLinks.map((v,n)=>n===i?{...v,kind:e.target.value}:v))}>{status.catalogues.linkKinds.map(k=><option key={k} value={k} disabled={profile.contactLinks.some((v,n)=>n!==i&&v.kind===k)}>{{discord:"Discord",linkedin:"LinkedIn",github:"GitHub",website:"Website"}[k]??k}</option>)}</select>
    <input aria-label={`Contact ${i+1} username or URL`} placeholder="Username or URL" value={link.value} maxLength={200} onChange={e=>set("contactLinks",profile.contactLinks.map((v,n)=>n===i?{...v,value:e.target.value}:v))}/>
    <button type="button" className="editor-remove" aria-label={`Remove contact ${i+1}`} onClick={()=>set("contactLinks",profile.contactLinks.filter((_,n)=>n!==i))}>×</button></div>)}
   {profile.contactLinks.length<3?<button type="button" className="link" onClick={()=>set("contactLinks",[...profile.contactLinks,{kind:status.catalogues.linkKinds.find(k=>!profile.contactLinks.some(v=>v.kind===k))??"discord",value:""}])}>+ Add contact</button>:null}
   <span id="contactLinks-error" className="directory-error">{errors.contactLinks?.join(" ")}</span></fieldset>
 </section>
 </fieldset>
 <footer className="editor-foot"><p role="status" className="quiet">{error?<span role="alert" className="directory-error">{error}</span>:null}{/* The missing-fields hint stays beside "Saved.", or a save reads as "you're live". */}{error?null:[dirty?"Unsaved changes.":message,missing.length?`To appear in the directory, add: ${missing.join(", ")}.`:""].filter(Boolean).join(" ")}</p>
  <button type="submit" className="primary" disabled={busy||archived}>{busy?"Saving…":"Save profile"}</button></footer>
 {crop?<PhotoCrop file={crop} onCancel={()=>setCrop(null)} onDone={f=>void photo(f)}/>:null}
 </form>}
 </>
}
