ALTER TABLE notify.campaigns
    ADD COLUMN email_settings jsonb,
    ADD COLUMN revision integer NOT NULL DEFAULT 0,
    ADD COLUMN updated_by uuid REFERENCES identity.people (id),
    ADD CONSTRAINT campaign_settings_object CHECK (email_settings IS NULL OR jsonb_typeof(email_settings) = 'object');
