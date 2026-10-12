-- A reopened application awaits a new decision. The original decision and
-- the reopening remain in the append-only status history. No rows are backfilled.
CREATE OR REPLACE FUNCTION applications.stamp_status_timestamps() RETURNS trigger AS $$
DECLARE
    actor uuid := NULLIF(current_setting('app.actor_id', true), '')::uuid;
BEGIN
    IF NEW.status IS NOT DISTINCT FROM OLD.status THEN
        RETURN NEW;
    END IF;

    IF OLD.status = 'rejected' AND NEW.status = 'under_review' THEN
        NEW.decided_at := NULL;
        NEW.decided_by := NULL;
    ELSIF NEW.status = 'submitted' THEN
        NEW.submitted_at := now();
    ELSIF NEW.status IN ('accepted', 'rejected', 'waitlisted') THEN
        NEW.decided_at := now();
        NEW.decided_by := actor;
    ELSIF NEW.status = 'confirmed' THEN
        NEW.confirmed_at := now();
    ELSIF NEW.status = 'declined' THEN
        NEW.declined_at := now();
    ELSIF NEW.status = 'checked_in' THEN
        NEW.checked_in_at := now();
        NEW.checked_in_by := actor;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
