import React, { useCallback, useEffect, useRef, useState } from 'react'
import { BadgeCheck, ChevronDown, ChevronRight, MessageSquare, Pencil, Star, ThumbsDown, ThumbsUp, Trash2 } from 'lucide-react'
import { SelectMenu } from './AuthModal'
import { apiRequest } from '../../models/api'
import { formatDate, formatNumber, t } from '../../models/i18n'

// Reviews and star ratings for one bus: the rating summary, a form to post a review, and each review with its
// replies and like/dislike buttons. The server takes the author from the sign-in token and decides who may edit
// or delete; the buttons shown here only follow what it allows. Review text is rendered as plain text.

export const MAX_REVIEW_LENGTH = 1000
const PAGE_SIZE = 20
// Reviews shown before "Show all reviews", so a busy bus does not push the rest of the page down.
export const PREVIEW_COUNT = 3
// Longer reviews are cut here with a "Read more".
const CLAMP_AT = 260
// Replies shown under a review before "Show more replies".
const REPLY_PREVIEW = 2
export const REVIEW_SORTS = [['newest', 'Most recent'], ['helpful', 'Most helpful'], ['highest', 'Highest rated'], ['lowest', 'Lowest rated']]
const RATING_WORDS = { 1: 'Poor', 2: 'Fair', 3: 'Good', 4: 'Very good', 5: 'Excellent' }

const errorText = (error) => t(error?.message || 'Something went wrong. Please try again.')
const idOf = (item) => String(item?._id || item?.id || '')
const json = (value) => JSON.stringify(value)
export const reviewApi = {
  list: (busId, token, page = 1, sort = 'newest') => apiRequest(`/api/reviews/bus/${encodeURIComponent(busId)}?page=${page}&pageSize=${PAGE_SIZE}&sort=${sort}`, { token }),
  create: (busId, rating, comment, token) => apiRequest('/api/reviews', { token, method: 'POST', body: json({ busId, rating, comment }) }),
  update: (reviewId, changes, token) => apiRequest(`/api/reviews/${encodeURIComponent(reviewId)}`, { token, method: 'PUT', body: json(changes) }),
  remove: (reviewId, token) => apiRequest(`/api/reviews/${encodeURIComponent(reviewId)}`, { token, method: 'DELETE' }),
  reply: (reviewId, busId, comment, token) => apiRequest(`/api/reviews/${encodeURIComponent(reviewId)}/reply`, { token, method: 'POST', body: json({ busId, comment }) }),
  removeReply: (replyId, token) => apiRequest(`/api/reviews/replies/${encodeURIComponent(replyId)}`, { token, method: 'DELETE' }),
  react: (reviewId, reaction, token) => apiRequest(`/api/reviews/${encodeURIComponent(reviewId)}/${reaction}`, { token, method: 'POST' }),
}

// An error message for a review or reply, or '' when it can be sent.
export function validateReview(rating, comment, { needsRating = true } = {}) {
  if (needsRating && !(Number.isInteger(rating) && rating >= 1 && rating <= 5)) return t('Choose a rating from 1 to 5 stars.')
  if (!comment.trim()) return t('Please write a few words.')
  if (comment.trim().length > MAX_REVIEW_LENGTH) return t('A review can be at most 1000 characters.')
  return ''
}

export function Stars({ value, size = 14, label }) {
  // Filled to the nearest whole star; the exact value is in the label (and shown as a number where it matters).
  const filled = Math.round(value || 0)
  return <span className="review-stars" role="img" aria-label={label || t('{value} out of 5 stars', { value: formatNumber(value || 0, { maximumFractionDigits: 1 }) })}>
    {[1, 2, 3, 4, 5].map((n) => <Star key={n} size={size} className={n <= filled ? 'on' : ''} aria-hidden="true" />)}
  </span>
}

// Five stars as a radio group: click a star, or use the arrow keys.
export function StarPicker({ value, onChange, label = t('Your rating'), disabled }) {
  const refs = useRef([])
  const choose = (n) => { onChange(n); refs.current[n - 1]?.focus() }
  const onKey = (event, n) => {
    if (event.key === 'ArrowRight' || event.key === 'ArrowUp') { event.preventDefault(); choose(Math.min(5, n + 1)) }
    if (event.key === 'ArrowLeft' || event.key === 'ArrowDown') { event.preventDefault(); choose(Math.max(1, n - 1)) }
  }
  return <div className="star-picker" role="radiogroup" aria-label={label}>
    {[1, 2, 3, 4, 5].map((n) => <button key={n} type="button" role="radio" ref={(el) => { refs.current[n - 1] = el }} aria-checked={value === n}
      aria-label={t('{count} stars', { count: n })} tabIndex={value ? (value === n ? 0 : -1) : (n === 1 ? 0 : -1)} disabled={disabled}
      className={n <= (value || 0) ? 'on' : ''} onClick={() => onChange(n)} onKeyDown={(e) => onKey(e, n)}><Star size={20} aria-hidden="true" /></button>)}
    <span className="star-picker-label">{value ? `${t('{count} of 5', { count: value })} · ${t(RATING_WORDS[value])}` : t('Tap a star')}</span>
  </div>
}

function Summary({ summary }) {
  if (!summary || !summary.totalRatings) return <div className="review-empty-summary"><span className="review-empty-icon" aria-hidden="true"><Star size={18} /></span><div><b>{t('No ratings yet')}</b><span>{t('No ratings yet. Be the first to review this bus.')}</span></div></div>
  const total = summary.totalRatings || 1
  return <div className="review-summary" data-testid="rating-summary">
    <div className="review-average">
      <strong>{formatNumber(summary.averageRating, { minimumFractionDigits: 1, maximumFractionDigits: 1 })}<small> / 5</small></strong>
      <Stars value={summary.averageRating} size={17} />
      <span>{t('{count} ratings', { count: summary.totalRatings })}</span>
    </div>
    <ul className="review-distribution" aria-label={t('Ratings by stars')}>
      {[5, 4, 3, 2, 1].map((n) => { const count = summary.distribution?.[n] || 0; return <li key={n}>
        <span>{n}<Star size={11} aria-hidden="true" /></span><i><b style={{ width: `${(count / total) * 100}%` }} /></i><span>{formatNumber(count)}</span>
      </li> })}
    </ul>
  </div>
}

// The same colour for a person every time, picked from their name.
const AVATAR_TONES = 6
const toneOf = (text = '') => [...text].reduce((sum, ch) => sum + ch.charCodeAt(0), 0) % AVATAR_TONES

function Author({ author, fallback, when, edited, badge }) {
  const name = author?.name || fallback || t('Commuter')
  return <div className="review-author">
    <span className={`review-avatar tone-${toneOf(name)}`} aria-hidden="true">{name.trim()?.[0]?.toUpperCase() || 'R'}</span>
    <div>
      <b>{name}{author?.verified && <BadgeCheck size={13} aria-label={t('Verified student')} />}{badge && <span className="review-you">{badge}</span>}</b>
      <small>{formatDate(when)}{edited ? ` · ${t('edited')}` : ''}</small>
    </div>
  </div>
}

// Long text shows its first lines with "Read more".
function ClampedText({ text }) {
  const [full, setFull] = useState(false)
  if (text.length <= CLAMP_AT) return <p className="review-text">{text}</p>
  return <p className="review-text">{full ? text : `${text.slice(0, CLAMP_AT).trimEnd()}…`}{' '}
    <button type="button" className="review-read-more" onClick={() => setFull(!full)} aria-expanded={full}>{full ? t('Show less') : t('Read more')}</button></p>
}

function ReviewItem({ review, me, busId, signedIn, token, notify, onNeedSignIn, onChanged, onDeleted, pinned = false }) {
  const mine = Boolean(me) && String(review.author?.id || review.userId) === me
  const [showAllReplies, setShowAllReplies] = useState(false)
  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState({ rating: review.rating || 0, comment: review.comment })
  const [replying, setReplying] = useState(false)
  const [replyText, setReplyText] = useState('')
  const [busy, setBusy] = useState('') // '' | 'save' | 'delete' | 'reply' | 'react'
  const [error, setError] = useState('')

  const run = async (kind, action) => {
    if (busy) return
    setBusy(kind); setError('')
    try { await action() } catch (e) { setError(errorText(e)) } finally { setBusy('') }
  }
  const react = (reaction) => {
    if (!signedIn) { onNeedSignIn(); return }
    run('react', async () => {
      const data = await reviewApi.react(idOf(review), reaction, token)
      onChanged({ ...review, likes: data.likes, dislikes: data.dislikes, myReaction: data.myReaction })
    })
  }
  const save = (event) => {
    event.preventDefault()
    const problem = validateReview(draft.rating, draft.comment)
    if (problem) { setError(problem); return }
    run('save', async () => {
      const data = await reviewApi.update(idOf(review), { rating: draft.rating, comment: draft.comment.trim() }, token)
      onChanged(data.review, data.summary)
      setEditing(false)
      notify(t('Review updated.'))
    })
  }
  const remove = () => {
    if (!window.confirm(t('Delete your review? Its replies will be deleted too.'))) return
    run('delete', async () => {
      const data = await reviewApi.remove(idOf(review), token)
      onDeleted(idOf(review), data.summary)
      notify(t('Review deleted.'))
    })
  }
  const sendReply = (event) => {
    event.preventDefault()
    const problem = validateReview(0, replyText, { needsRating: false })
    if (problem) { setError(problem); return }
    run('reply', async () => {
      const data = await reviewApi.reply(idOf(review), busId, replyText.trim(), token)
      const replies = [...(review.repliesList || []).filter((r) => idOf(r) !== idOf(data.reply)), data.reply]
      onChanged({ ...review, repliesList: replies, replies: replies.length })
      setReplyText(''); setReplying(false); setShowAllReplies(true)
      notify(t('Reply added.'))
    })
  }
  const removeReply = (reply) => {
    if (!window.confirm(t('Delete your reply?'))) return
    run('reply', async () => {
      await reviewApi.removeReply(idOf(reply), token)
      const replies = (review.repliesList || []).filter((r) => idOf(r) !== idOf(reply))
      onChanged({ ...review, repliesList: replies, replies: replies.length })
    })
  }

  const replies = review.repliesList || []
  const shownReplies = showAllReplies ? replies : replies.slice(0, REPLY_PREVIEW)
  return <article className={`review-item${pinned ? ' is-mine' : ''}`} data-testid="review">
    <div className="review-head">
      <Author author={review.author} fallback={review.userName} when={review.updatedAt || review.createdAt} edited={review.edited} badge={pinned ? t('Your review') : ''} />
      {review.rating ? <span className="review-score"><Stars value={review.rating} /><small>{t(RATING_WORDS[review.rating])}</small></span> : null}
    </div>
    {editing ? <form className="review-form" onSubmit={save} aria-label={t('Edit your review')}>
      <StarPicker value={draft.rating} onChange={(rating) => setDraft({ ...draft, rating })} disabled={busy === 'save'} />
      <textarea value={draft.comment} maxLength={MAX_REVIEW_LENGTH} onChange={(e) => setDraft({ ...draft, comment: e.target.value })} aria-label={t('Your review')} rows={3} />
      <div className="review-form-actions">
        <button type="submit" className="extra-button" disabled={busy === 'save'}>{busy === 'save' ? t('Saving…') : t('Save changes')}</button>
        <button type="button" className="extra-button secondary" onClick={() => { setEditing(false); setDraft({ rating: review.rating || 0, comment: review.comment }); setError('') }}>{t('Cancel')}</button>
      </div>
    </form> : review.comment ? <ClampedText text={review.comment} /> : <p className="review-text muted">{t('Rated without a comment.')}</p>}
    {error && <p className="review-error" role="alert">{error}</p>}
    {!editing && <div className="review-actions">
      <button type="button" className={review.myReaction === 'like' ? 'active' : ''} aria-pressed={review.myReaction === 'like'} disabled={busy === 'react'} onClick={() => react('like')} aria-label={t('Like ({count})', { count: review.likes || 0 })}><ThumbsUp size={14} aria-hidden="true" /> {formatNumber(review.likes || 0)}</button>
      <button type="button" className={review.myReaction === 'dislike' ? 'active' : ''} aria-pressed={review.myReaction === 'dislike'} disabled={busy === 'react'} onClick={() => react('dislike')} aria-label={t('Dislike ({count})', { count: review.dislikes || 0 })}><ThumbsDown size={14} aria-hidden="true" /> {formatNumber(review.dislikes || 0)}</button>
      <button type="button" onClick={() => signedIn ? setReplying(!replying) : onNeedSignIn()} aria-expanded={replying}><MessageSquare size={14} aria-hidden="true" /> {t('Reply')}</button>
      {mine && <span className="review-actions-own">
        <button type="button" onClick={() => { setEditing(true); setError('') }}><Pencil size={14} aria-hidden="true" /> {t('Edit')}</button>
        <button type="button" className="danger" disabled={busy === 'delete'} onClick={remove}><Trash2 size={14} aria-hidden="true" /> {busy === 'delete' ? t('Deleting…') : t('Delete')}</button>
      </span>}
    </div>}
    {(replies.length > 0 || replying) && <div className="review-replies" aria-label={t('Replies')}>
      {shownReplies.map((reply) => <div className="review-reply" key={idOf(reply)} data-testid="reply">
        <Author author={reply.author} fallback={reply.userName} when={reply.createdAt} />
        <p className="review-text">{reply.comment}</p>
        {Boolean(me) && String(reply.author?.id || reply.userId) === me && <button type="button" className="review-reply-delete" onClick={() => removeReply(reply)} aria-label={t('Delete your reply')}><Trash2 size={13} /></button>}
      </div>)}
      {replies.length > REPLY_PREVIEW && <button type="button" className="review-read-more" onClick={() => setShowAllReplies(!showAllReplies)} aria-expanded={showAllReplies}>
        {showAllReplies ? t('Show fewer replies') : t('Show {count} more replies', { count: replies.length - REPLY_PREVIEW })}</button>}
      {replying && <form className="review-form reply" onSubmit={sendReply}>
        <textarea value={replyText} maxLength={MAX_REVIEW_LENGTH} onChange={(e) => setReplyText(e.target.value)} placeholder={t('Write a reply…')} aria-label={t('Your reply')} rows={2} />
        <div className="review-form-actions">
          <button type="submit" className="extra-button" disabled={busy === 'reply'}>{busy === 'reply' ? t('Sending…') : t('Post reply')}</button>
          <button type="button" className="extra-button secondary" onClick={() => { setReplying(false); setReplyText(''); setError('') }}>{t('Cancel')}</button>
        </div>
      </form>}
    </div>}
  </article>
}

// The rating summary, the reader's own review (or a form to write one: one review per person per bus), and the
// other reviews: a short preview first, then the full list in its own scrolling box, sortable and paged.
export function BusReviews({ bus, user, token, signedIn, notify, openAuth, defaultOpen = false }) {
  const busId = idOf(bus)
  const me = String(user?.id || user?._id || '')
  const [open, setOpen] = useState(defaultOpen)
  const [reviews, setReviews] = useState([])
  const [myReview, setMyReview] = useState(null)
  const [summary, setSummary] = useState(null)
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [totalPages, setTotalPages] = useState(1)
  const [sort, setSort] = useState('newest')
  const [expanded, setExpanded] = useState(false)
  const [status, setStatus] = useState('idle') // idle | loading | ready | error
  const [loadingMore, setLoadingMore] = useState(false)
  const [error, setError] = useState('')
  const [rating, setRating] = useState(0)
  const [comment, setComment] = useState('')
  const [formError, setFormError] = useState('')
  const [saving, setSaving] = useState(false)

  const load = useCallback(async () => {
    // A re-sort keeps the current list on screen while the new order loads.
    setStatus((current) => current === 'ready' ? 'ready' : 'loading')
    try {
      const data = await reviewApi.list(busId, token, 1, sort)
      const list = data.reviews || []
      setReviews(list); setSummary(data.summary || null); setPage(1); setTotalPages(data.totalPages || 1)
      setTotal(data.total ?? list.length)
      // Older servers do not send myReview; fall back to finding it in the page.
      setMyReview(data.myReview ?? (me ? list.find((r) => String(r.author?.id || r.userId) === me) : null) ?? null)
      setStatus('ready'); setError('')
    } catch (e) { setStatus('error'); setError(errorText(e)) }
  }, [busId, token, sort, me])
  useEffect(() => { if (open) load() }, [open, load])

  const loadMore = async () => {
    if (loadingMore) return
    setLoadingMore(true)
    try {
      const data = await reviewApi.list(busId, token, page + 1, sort)
      setReviews((current) => [...current, ...(data.reviews || []).filter((r) => !current.some((c) => idOf(c) === idOf(r)))])
      setPage(page + 1); setTotalPages(data.totalPages || 1)
    } catch (e) { setError(errorText(e)) } finally { setLoadingMore(false) }
  }

  const needSignIn = () => { if (openAuth) openAuth(); else setError(t('Sign in to leave a review.')) }
  const submit = async (event) => {
    event.preventDefault()
    if (!signedIn) { needSignIn(); return }
    if (saving) return
    const problem = validateReview(rating, comment)
    if (problem) { setFormError(problem); return }
    setSaving(true); setFormError('')
    try {
      const data = await reviewApi.create(busId, rating, comment.trim(), token)
      setMyReview(data.review)
      setTotal((n) => n + 1)
      if (data.summary) setSummary(data.summary)
      setRating(0); setComment('')
      notify(t('Review posted.'))
    } catch (e) {
      setFormError(errorText(e))
      // Already reviewed (from another tab, say): load that review so it can be edited instead.
      if (e.status === 409) load()
    } finally { setSaving(false) }
  }
  const changed = (review, newSummary) => {
    setReviews((current) => current.map((r) => idOf(r) === idOf(review) ? { ...r, ...review } : r))
    setMyReview((current) => current && idOf(current) === idOf(review) ? { ...current, ...review } : current)
    if (newSummary) setSummary(newSummary)
  }
  const deleted = (reviewId, newSummary) => {
    setReviews((current) => current.filter((r) => idOf(r) !== reviewId))
    setMyReview((current) => current && idOf(current) === reviewId ? null : current)
    setTotal((n) => Math.max(0, n - 1))
    if (newSummary) setSummary(newSummary)
  }

  const itemProps = { me, busId, signedIn, token, notify, onNeedSignIn: needSignIn, onChanged: changed, onDeleted: deleted }
  const others = reviews.filter((r) => !myReview || idOf(r) !== idOf(myReview))
  const othersTotal = Math.max(others.length, total - (myReview ? 1 : 0))
  const shown = expanded ? others : others.slice(0, PREVIEW_COUNT)
  const average = summary?.totalRatings ? summary.averageRating : null
  const busName = bus.busName ? t(bus.busName) : t('this bus')

  return <div className="bus-reviews">
    <button type="button" className="review-toggle" onClick={() => setOpen(!open)} aria-expanded={open}>
      {open ? t('Hide community reviews') : t('Community reviews')}
      {average !== null && <span className="review-toggle-score"><Star size={12} aria-hidden="true" /> {formatNumber(average, { minimumFractionDigits: 1, maximumFractionDigits: 1 })} · {t('{count} ratings', { count: summary.totalRatings })}</span>}
      <ChevronRight size={13} />
    </button>
    {open && <div className="review-content">
      {status === 'loading' ? <div className="extra-loading"><i />{t('Loading reviews…')}</div>
        : status === 'error' ? <div className="review-load-error"><p className="extra-feedback error">{error}</p><button type="button" className="extra-button secondary" onClick={load}>{t('Try again')}</button></div>
          : <>
            <Summary summary={summary} />

            {!signedIn ? <div className="review-signin"><span><b>{t('Travelled on {name}?', { name: busName })}</b>{t('Sign in to rate this bus and write a review.')}</span><button type="button" className="extra-button" onClick={needSignIn}>{t('Sign in')}</button></div>
              : myReview ? <section className="review-mine" aria-label={t('Your review')}>
                <p className="review-mine-note"><BadgeCheck size={14} aria-hidden="true" /> {t('You have reviewed this bus. You can edit or delete your review at any time.')}</p>
                <ReviewItem key={idOf(myReview)} review={myReview} pinned {...itemProps} />
              </section>
                : <form className="review-form review-composer" onSubmit={submit} noValidate aria-label={t('Write a review')}>
                  <div className="review-composer-head"><b>{t('Rate {name}', { name: busName })}</b><small>{t('Your review helps other commuters choose the right bus.')}</small></div>
                  <StarPicker value={rating} onChange={(n) => { setRating(n); setFormError('') }} disabled={saving} />
                  <textarea value={comment} maxLength={MAX_REVIEW_LENGTH} onChange={(e) => { setComment(e.target.value); setFormError('') }} rows={3}
                    placeholder={t('Review {name}…', { name: busName })} aria-label={t('Your review')} />
                  {formError && <p className="review-error" role="alert">{formError}</p>}
                  <div className="review-form-actions">
                    <button type="submit" className="extra-button" disabled={saving}>{saving ? t('Posting…') : t('Post review')}</button>
                    <small>{t('{count} characters left', { count: MAX_REVIEW_LENGTH - comment.length })}</small>
                  </div>
                </form>}

            {error && <p className="review-error" role="alert">{error}</p>}

            {othersTotal > 0 && <div className="review-list-head">
              <b>{myReview ? t('Other reviews') : t('Reviews')} <span>{formatNumber(othersTotal)}</span></b>
              {othersTotal > 1 && <div className="review-sort"><span id={`review-sort-${busId}`}>{t('Sort by')}</span>
                <SelectMenu labelledBy={`review-sort-${busId}`} value={sort} onChange={(next) => { setSort(next); setExpanded(false) }}
                  options={REVIEW_SORTS.map(([value, label]) => ({ value, label: t(label) }))} /></div>}
            </div>}
            {shown.length ? <div className={`review-list${expanded ? ' is-expanded' : ''}`} data-testid="review-list">
              {shown.map((review) => <ReviewItem key={idOf(review)} review={review} {...itemProps} />)}
              {expanded && page < totalPages && <button type="button" className="extra-button secondary review-more" onClick={loadMore} disabled={loadingMore}>{loadingMore ? t('Loading…') : t('Show more reviews')}</button>}
            </div> : !myReview && <p className="extra-empty-copy">{t('No reviews yet.')}</p>}
            {othersTotal > PREVIEW_COUNT && <button type="button" className="review-show-all" onClick={() => setExpanded(!expanded)} aria-expanded={expanded}>
              {expanded ? t('Show fewer reviews') : t('Show all {count} reviews', { count: formatNumber(othersTotal) })}<ChevronDown size={15} aria-hidden="true" /></button>}
          </>}
    </div>}
  </div>
}

