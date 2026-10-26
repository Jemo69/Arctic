import {redirect} from "next/navigation";
// The editor lives in a modal over the directory now; this keeps old links working.
export default function ProfilePage(){redirect("/portal/directory?edit=1")}
