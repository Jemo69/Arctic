CREATE TABLE notify.message_tracking (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    message_id uuid NOT NULL UNIQUE REFERENCES notify.messages(id) ON DELETE CASCADE,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE notify.email_engagement_events (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    message_id uuid NOT NULL REFERENCES notify.messages(id) ON DELETE CASCADE,
    link_id uuid REFERENCES notify.tracked_links(id) ON DELETE CASCADE,
    kind text NOT NULL CHECK (kind IN ('open', 'click')),
    occurred_at timestamptz NOT NULL DEFAULT now(),
    browser text NOT NULL,
    operating_system text NOT NULL,
    platform text NOT NULL,
    automated boolean NOT NULL DEFAULT false,
    country_code varchar(2),
    CHECK ((kind = 'open' AND link_id IS NULL) OR (kind = 'click' AND link_id IS NOT NULL))
);

CREATE INDEX email_engagement_message_kind_time_idx
    ON notify.email_engagement_events (message_id, kind, occurred_at);
