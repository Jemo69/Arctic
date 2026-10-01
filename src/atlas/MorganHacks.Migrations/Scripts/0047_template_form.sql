-- The form an email is about.
--
-- A template that says "applications close on the fifteenth, apply here" has
-- to name two things it currently cannot: the deadline, which 0020's event
-- dates now cover, and the link, which is this. Until now an author typed the
-- share URL into the body, which is a URL in a database row -- so the same
-- template on staging points at the production form, and the preview and the
-- test send both look right.
--
-- ------------------------------------------------------ one form, not many ---
--
-- A single column rather than a join table. A template is one email about one
-- thing, and the version that let a template name several forms needed a slot
-- name per form -- {{form.apply.link}} against {{form.feedback.link}} -- which
-- is a naming decision an author has to make before they can write a sentence.
-- One form means the names are just {{form.link}} and {{form.name}}, and the
-- group only appears in the editor's menu once a form is chosen.
--
-- ------------------------------------------------------------- on deletion ---
--
-- ON DELETE SET NULL, and 0038 means a form can also be removed without being
-- deleted. Either way the binding goes empty rather than dangling, every
-- {{form.*}} placeholder stops resolving, and the campaign's unfillable check
-- refuses the send. That is the right failure: a dead link inside an approved
-- broadcast is the one thing worse than a broadcast that will not go out.
--
-- Nullable, because almost every template has nothing to do with a form. A
-- sign-in link, a decision, a welcome -- none of them name one, and demanding
-- a form at creation would mean inventing a binding to get past a form field.
ALTER TABLE notify.templates
    ADD COLUMN form_id uuid REFERENCES applications.forms (id) ON DELETE SET NULL;

-- Read whenever a campaign renders, which is once per send rather than once
-- per recipient -- but also by the editor on the screen where somebody picks
-- the form, and that is the one a person is waiting on.
CREATE INDEX templates_form_idx ON notify.templates (form_id)
    WHERE form_id IS NOT NULL;
