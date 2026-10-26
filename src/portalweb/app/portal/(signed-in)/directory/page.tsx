import {directoryContext} from "@/lib/directory-server";
import {BrowseDirectory} from "./browse";
import {ProfileModal} from "./profile-modal";
import {SelfCard} from "./hacker-card";
export default async function DirectoryPage(){const {status,own}=await directoryContext();return <><h1>Find your <span>teammates</span></h1><p>Meet hackers joining MorganHacks.</p>{!status.eligible?<p className="notice">{status.reason}</p>:<>{status.phase==="open"?<BrowseDirectory status={status} self={own?<SelfCard own={own}/>:null} selfId={own?.profile?.id}/>:<>{own?<div className="directory-grid"><SelfCard own={own}/></div>:null}<p className="notice">{status.phase==="archived"?"The directory has closed.":status.opensAt?`The directory opens ${new Date(status.opensAt).toLocaleDateString("en-US",{timeZone:"America/New_York"})}.`:"The directory opening date has not been announced."}</p></>}{own?<ProfileModal status={status} own={own}/>:null}</>}</>}
