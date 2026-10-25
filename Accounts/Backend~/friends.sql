-- Apply once in the existing Supabase project's SQL Editor. No client administrative keys.
begin;
create table if not exists public.game_profiles (
  id uuid primary key references auth.users(id) on delete cascade,
  code text not null unique default upper(substr(replace(gen_random_uuid()::text, '-', ''), 1, 12)),
  name text not null check (char_length(name) between 1 and 24)
);
create table if not exists public.game_friendships (
  low_id uuid not null references public.game_profiles(id) on delete cascade,
  high_id uuid not null references public.game_profiles(id) on delete cascade,
  requested_by uuid not null,
  accepted boolean not null default false,
  created_at timestamptz not null default now(),
  primary key(low_id, high_id),
  check (low_id < high_id and requested_by in (low_id, high_id))
);
alter table public.game_profiles enable row level security;
alter table public.game_friendships enable row level security;
revoke all on public.game_profiles, public.game_friendships from public, anon, authenticated;

create or replace function public.game_social(operation text, target text default null, nickname text default null)
returns jsonb language plpgsql security definer set search_path = '' as $$
declare
  me uuid := auth.uid(); other uuid; pair_low uuid; pair_high uuid; result jsonb;
begin
  if me is null or not exists (select 1 from auth.users where id = me and email is not null and email <> '' and email_confirmed_at is not null) then
    raise insufficient_privilege using message = 'Sign in with a verified account';
  end if;
  if operation not in ('list', 'profile', 'request', 'accept', 'remove') or operation is null then
    raise invalid_parameter_value using message = 'Invalid operation';
  end if;
  -- Serialize actions by this player; table PK also prevents simultaneous reciprocal duplicates.
  perform pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended(me::text, 0));
  insert into public.game_profiles(id, name) values (me, '冒險者' || substr(me::text, 1, 6)) on conflict (id) do nothing;
  if operation = 'profile' then
    if nickname is null or char_length(trim(nickname)) not between 1 and 24 then raise invalid_parameter_value using message = 'Invalid name'; end if;
    update public.game_profiles set name = trim(nickname) where id = me;
  elsif operation = 'request' then
    if target is null or target !~ '^[0-9A-F]{12}$' then raise invalid_parameter_value using message = 'Invalid code'; end if;
    select id into other from public.game_profiles where code = target;
    if other is null or other = me then raise invalid_parameter_value using message = 'Invalid code'; end if;
    if (select count(*) from public.game_friendships where me in (low_id, high_id)) >= 100 or
       (select count(*) from public.game_friendships where other in (low_id, high_id)) >= 100 then
      raise program_limit_exceeded using message = 'Friend limit';
    end if;
    pair_low := least(me, other); pair_high := greatest(me, other);
    insert into public.game_friendships(low_id, high_id, requested_by) values(pair_low, pair_high, me)
      on conflict (low_id, high_id) do nothing;
  elsif operation in ('accept', 'remove') then
    begin other := target::uuid; exception when invalid_text_representation then raise invalid_parameter_value using message = 'Invalid player'; end;
    if other is null or me = other then raise invalid_parameter_value using message = 'Invalid player'; end if;
    pair_low := least(me, other); pair_high := greatest(me, other);
    if operation = 'accept' then
      update public.game_friendships set accepted = true where low_id = pair_low and high_id = pair_high and requested_by <> me;
      if not found then raise invalid_parameter_value using message = 'Invite not received'; end if;
    else
      delete from public.game_friendships where low_id = pair_low and high_id = pair_high;
    end if;
  end if;
  select jsonb_build_object('code', p.code, 'name', p.name,
    'friends', coalesce((select jsonb_agg(jsonb_build_object('id', q.id, 'code', q.code, 'name', q.name) order by q.name)
      from public.game_friendships f join public.game_profiles q on q.id = case when f.low_id = me then f.high_id else f.low_id end
      where me in (f.low_id, f.high_id) and f.accepted), '[]'::jsonb),
    'incoming', coalesce((select jsonb_agg(jsonb_build_object('id', q.id, 'code', q.code, 'name', q.name) order by f.created_at)
      from public.game_friendships f join public.game_profiles q on q.id = f.requested_by
      where me in (f.low_id, f.high_id) and not f.accepted and f.requested_by <> me), '[]'::jsonb),
    'outgoing', coalesce((select jsonb_agg(jsonb_build_object('id', q.id, 'code', q.code, 'name', q.name) order by f.created_at)
      from public.game_friendships f join public.game_profiles q on q.id = case when f.low_id = me then f.high_id else f.low_id end
      where me in (f.low_id, f.high_id) and not f.accepted and f.requested_by = me), '[]'::jsonb)) into result
    from public.game_profiles p where p.id = me;
  return result;
end;
$$;
revoke all on function public.game_social(text, text, text) from public, anon;
grant execute on function public.game_social(text, text, text) to authenticated;
-- Make the RPC visible immediately after this transaction commits, including re-runs.
notify pgrst, 'reload schema';
commit;
