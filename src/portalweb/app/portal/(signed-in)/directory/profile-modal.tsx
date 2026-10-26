"use client";
import {useLayoutEffect,useRef} from "react";
import {useRouter,useSearchParams} from "next/navigation";
import type {DirectoryStatus,OwnProfile} from "@/lib/directory";
import {ProfileEditor} from "./profile/profile-editor";
import {Materialize} from "./profile/materialize";

/** The profile editor as a modal over the directory, opened by `?edit=1` so other pages can link straight to it. */
export function ProfileModal({status,own}:{status:DirectoryStatus;own:OwnProfile}){
 const params=useSearchParams();const router=useRouter();const open=params.has("edit");const dialog=useRef<HTMLDialogElement>(null);const dirty=useRef(false);
 // A layout effect, so the dialog is open before Materialize measures it in its own (passive) effect.
 useLayoutEffect(()=>{if(open)dialog.current?.showModal();else{dialog.current?.close();dirty.current=false}},[open]);
 function close(){if(dirty.current&&!window.confirm("Close without saving your profile?"))return;const p=new URLSearchParams(window.location.search);p.delete("edit");router.replace(`/portal/directory${p.size?`?${p}`:""}`,{scroll:false})}
 return <dialog className="profile-modal" ref={dialog} aria-labelledby="profile-modal-title" onCancel={e=>{e.preventDefault();close()}}>
 {open?<Materialize className="profile-modal__pane"><header className="profile-modal__head"><div><h2 id="profile-modal-title">Your hacker profile</h2><p className="quiet">Help other hackers find you as a teammate.</p></div><button type="button" className="profile-modal__close" aria-label="Close profile editor" onClick={close}><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18"/></svg></button></header>
 <ProfileEditor status={status} initial={own} onDirty={d=>{dirty.current=d}}/></Materialize>:null}
 </dialog>
}
