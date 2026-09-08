import { useEffect, useId, useMemo, useRef, useState } from 'react';

import './SearchableSelect.css';

export interface SearchableSelectOption {
  id: string;
  label: string;
  secondaryLabel?: string | null;
  disabled?: boolean;
}

interface SearchableSelectProps {
  label: string;
  value: string;
  options: readonly SearchableSelectOption[];
  onChange: (id: string) => void;
  searchValue: string;
  onSearchChange: (value: string) => void;
  searchLabel: string;
  chooseLabel: string;
  loadingLabel: string;
  noResultsLabel?: string;
  emptyOptionLabel?: string;
  disabled?: boolean;
  loading?: boolean;
}

export function SearchableSelect({
  label,
  value,
  options,
  onChange,
  searchValue,
  onSearchChange,
  searchLabel,
  chooseLabel,
  loadingLabel,
  noResultsLabel = '—',
  emptyOptionLabel,
  disabled = false,
  loading = false,
}: SearchableSelectProps) {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const searchInputRef = useRef<HTMLInputElement>(null);
  const popupId = useId();
  const selected = useMemo(() => options.find((option) => option.id === value) ?? null, [options, value]);

  useEffect(() => {
    if (!open) return;

    const closeOnOutsidePointer = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false);
    };
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setOpen(false);
    };

    document.addEventListener('pointerdown', closeOnOutsidePointer);
    document.addEventListener('keydown', closeOnEscape);
    window.requestAnimationFrame(() => searchInputRef.current?.focus());
    return () => {
      document.removeEventListener('pointerdown', closeOnOutsidePointer);
      document.removeEventListener('keydown', closeOnEscape);
    };
  }, [open]);

  const triggerText = selected?.label
    ?? (value === '' && emptyOptionLabel ? emptyOptionLabel : loading ? loadingLabel : chooseLabel);

  function select(id: string) {
    onChange(id);
    setOpen(false);
  }

  return (
    <div className="searchable-select" ref={rootRef}>
      <span className="field-label">{label}</span>
      <button
        type="button"
        className="searchable-select-trigger"
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={open ? popupId : undefined}
        disabled={disabled}
        onClick={() => setOpen((current) => !current)}
      >
        <span className="searchable-select-trigger-copy">
          <strong>{triggerText}</strong>
          {selected?.secondaryLabel && <small>{selected.secondaryLabel}</small>}
        </span>
        <span className="searchable-select-chevron" aria-hidden="true">⌄</span>
      </button>

      {open && (
        <div className="searchable-select-popup" id={popupId}>
          <label className="searchable-select-search">
            <span>{searchLabel}</span>
            <input
              ref={searchInputRef}
              type="search"
              value={searchValue}
              onChange={(event) => onSearchChange(event.target.value)}
            />
          </label>
          <div className="searchable-select-options" role="listbox" aria-label={label}>
            {emptyOptionLabel && (
              <button
                type="button"
                role="option"
                aria-selected={value === ''}
                className={value === '' ? 'is-selected' : ''}
                onClick={() => select('')}
              >
                {emptyOptionLabel}
              </button>
            )}
            {options.map((option) => (
              <button
                key={option.id}
                type="button"
                role="option"
                aria-selected={option.id === value}
                className={option.id === value ? 'is-selected' : ''}
                disabled={option.disabled}
                onClick={() => select(option.id)}
              >
                <span>{option.label}</span>
                {option.secondaryLabel && <small>{option.secondaryLabel}</small>}
              </button>
            ))}
            {!loading && options.length === 0 && <span className="searchable-select-empty">{noResultsLabel}</span>}
            {loading && <span className="searchable-select-empty">{loadingLabel}</span>}
          </div>
        </div>
      )}
    </div>
  );
}
