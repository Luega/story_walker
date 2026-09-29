/**
 * Soft blobs of colour behind every page, the way a picture book has a painted endpaper rather than
 * a blank one. Fixed and out of the way of pointers, so nothing on the page has to know about them.
 */
export function Backdrop() {
  return (
    <div aria-hidden className="pointer-events-none fixed inset-0 -z-10 overflow-hidden">
      <div className="absolute -top-32 -left-24 size-96 rounded-full bg-sun-soft opacity-80 blur-3xl" />
      <div className="absolute top-1/3 -right-32 size-[28rem] rounded-full bg-sky-soft opacity-80 blur-3xl" />
      <div className="absolute -bottom-40 left-1/4 size-[26rem] rounded-full bg-lilac-soft opacity-70 blur-3xl" />
    </div>
  );
}
