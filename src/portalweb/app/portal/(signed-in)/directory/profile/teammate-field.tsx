const suggestions = [
  "Someone strong in frontend",
  "A designer who enjoys UI/UX",
  "Help with backend and APIs",
  "Someone to brainstorm with",
  "A teammate who enjoys pitching",
  "Other beginners to learn with",
];
const maxLength = 200;

export function TeammateField({ value, errors, onChange }: {
  value: string;
  errors?: string[];
  onChange: (value: string) => void;
}) {
  function proposed(suggestion: string) {
    const current = value.trimEnd();
    return current ? `${current}${/[.!?;]$/.test(current) ? " " : "; "}${suggestion}` : suggestion;
  }

  return (
    <div className="directory-field">
      <label htmlFor="lookingFor">What I’m looking for in teammates</label>
      <input
        id="lookingFor"
        value={value}
        maxLength={maxLength}
        placeholder="What would complement your skills?"
        onChange={event => onChange(event.target.value)}
        aria-invalid={!!errors?.length}
        aria-describedby="lookingFor-count lookingFor-help lookingFor-error"
      />
      {value.length > maxLength * 0.8 ? <small id="lookingFor-count" className="editor-count" aria-live="polite">{value.length} / {maxLength}</small> : null}
      <small id="lookingFor-help" className="visually-hidden">Add suggestions below or write your own.</small>
      <div className="directory-suggestions" role="group" aria-label="Teammate suggestions">
        {suggestions.map(suggestion => {
          const added = value.toLowerCase().includes(suggestion.toLowerCase());
          const next = proposed(suggestion);
          const tooLong = next.length > maxLength;
          return (
            <button
              key={suggestion}
              type="button"
              disabled={added || tooLong}
              title={added ? "Already added" : tooLong ? "Edit your text to make room for this suggestion" : "Add this suggestion"}
              onClick={() => {
                if (!added && !tooLong) onChange(next);
              }}
            >
              <span aria-hidden="true">{added ? "✓ " : "+ "}</span>{suggestion}
            </button>
          );
        })}
      </div>
      <span id="lookingFor-error" className="directory-error">{errors?.join(" ")}</span>
    </div>
  );
}
