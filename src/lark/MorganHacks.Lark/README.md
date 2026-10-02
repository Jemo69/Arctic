# MorganHacks.Lark

The entry point and the send loop: claims a batch from `notify.messages`,
sends each through the SES adapter in `Sending/`, sleeps when the queue is
empty.

Campaign and template definitions are not here — see
`MorganHacks.Lark.Data`, which this project depends on for all of it.
