import Link from "next/link";
import type {DirectoryCard,OwnProfile} from "@/lib/directory";
import {teamLabels} from "@/lib/directory";
export const statusLabel=(p:DirectoryCard)=>`${teamLabels[p.teamStatus??""]??"Team status not set"}${p.teamStatus==="needMembers"&&p.teammatesNeeded?` · ${p.teammatesNeeded} needed`:""}`;
// The status comes last so the Heist theme can sit it at the bottom of the card as a stamp.
export function HackerCard({profile}:{profile:DirectoryCard}){return <article className="hacker-card" data-status={profile.teamStatus??undefined}>
 <span className="hacker-card__photo">{profile.photoUrl?<img src={profile.photoUrl} width={80} height={80} alt={`Photo of ${profile.displayName??"hacker"}`} />:<div className="hacker-avatar" aria-hidden="true">{profile.displayName?.slice(0,1)??"?"}</div>}</span>
 <h2>{profile.displayName||"Your name"}</h2><p>{[profile.school,profile.yearLevel].filter(Boolean).join(" · ")||"Your school"}</p><p className="hacker-card__headline">{profile.headline}</p>
 <ul className="directory-tags">{profile.skills.slice(0,2).map(s=><li key={s}>{s}</li>)}</ul>
 <p className="quiet">{profile.pace}</p><p className="hacker-card__status">{statusLabel(profile)}</p></article>}
/** The viewer's own card, first in the grid: what others see, a status dot, and the way into the editor. */
export function SelfCard({own}:{own:OwnProfile}){const state=own.state.replace(/([A-Z])/g," $1").toLowerCase();
 return <Link href="/portal/directory?edit=1" scroll={false} className="directory-card-button directory-self" data-state={own.state} aria-label={own.profile?`Edit your profile (${state})`:"Join the hacker directory"}>
 {own.profile?<HackerCard profile={{...own.profile,photoUrl:own.profile.photoUrl??own.photoUrl}}/>:<article className="hacker-card directory-self__empty"><div className="hacker-avatar" aria-hidden="true">+</div><h2>Join the directory</h2><p>Make a card so other hackers can find you.</p></article>}
 <span className="directory-self__badge"><i aria-hidden="true"/>{state}<b className="directory-self__edit"><svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 013 3L7 19l-4 1 1-4z"/></svg>{own.profile?"Edit":"Set up"}</b></span></Link>}
