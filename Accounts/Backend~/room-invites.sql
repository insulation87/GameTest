-- Run after friends.sql in the same Supabase SQL Editor. Re-runnable; no client admin keys.
begin;
create table if not exists public.game_invite_rooms (
  host_id uuid primary key references public.game_profiles(id) on delete cascade,
  room_code text not null check (room_code ~ '^[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{10}$'),
  room_password text not null check (char_length(room_password) <= 64),
  active_until timestamptz not null,
  unique(host_id, room_code)
);
create table if not exists public.game_room_invitations (
  id uuid primary key default gen_random_uuid(),
  host_id uuid not null,
  room_code text not null,
  recipient_id uuid not null references public.game_profiles(id) on delete cascade,
  expires_at timestamptz not null,
  foreign key(host_id, room_code) references public.game_invite_rooms(host_id, room_code) on delete cascade,
  unique(host_id, recipient_id),
  check(host_id <> recipient_id)
);
alter table public.game_invite_rooms enable row level security;
alter table public.game_room_invitations enable row level security;
revoke all on public.game_invite_rooms, public.game_room_invitations from public, anon, authenticated;

create or replace function public.game_room_invites(operation text, target text default null,
  room_code text default null, room_password text default null)
returns jsonb language plpgsql security definer set search_path = '' as $$
declare
  me uuid := auth.uid(); other uuid; selected_id uuid;
  accepted_code text; accepted_password text; result jsonb;
begin
  if me is null or not exists (select 1 from auth.users where id = me and email is not null and email <> '' and email_confirmed_at is not null) then
    raise insufficient_privilege using message = 'Verified sign in required';
  end if;
  if operation is null or operation not in ('list','invite','accept','decline','heartbeat','close') then
    raise invalid_parameter_value using message = 'Invalid operation';
  end if;
  perform pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended(me::text, 0));
  if operation = 'invite' then
    begin other := target::uuid; exception when invalid_text_representation then raise invalid_parameter_value using message = 'Invalid friend'; end;
    if other is null or other = me or not exists (select 1 from public.game_friendships f
      where f.low_id = least(me,other) and f.high_id = greatest(me,other) and f.accepted) then
      raise invalid_parameter_value using message = 'Accepted friend required';
    end if;
    if room_code is null or room_code !~ '^[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{10}$' or
       room_password is null or char_length(room_password) > 64 then
      raise invalid_parameter_value using message = 'Invalid room';
    end if;
    delete from public.game_invite_rooms r where r.host_id = me and r.room_code <> game_room_invites.room_code;
    insert into public.game_invite_rooms as r(host_id,room_code,room_password,active_until)
      values(me,room_code,room_password,now() + interval '60 seconds')
      on conflict(host_id) do update set room_password = excluded.room_password, active_until = excluded.active_until;
    insert into public.game_room_invitations as i(host_id,room_code,recipient_id,expires_at)
      values(me,room_code,other,now() + interval '5 minutes')
      on conflict(host_id,recipient_id) do update set id = gen_random_uuid(),
        room_code = excluded.room_code, expires_at = excluded.expires_at;
  elsif operation = 'heartbeat' then
    update public.game_invite_rooms r set active_until = now() + interval '60 seconds'
      where r.host_id = me and r.room_code = game_room_invites.room_code;
    if not found then raise invalid_parameter_value using message = 'Room closed'; end if;
  elsif operation = 'close' then
    delete from public.game_invite_rooms r where r.host_id = me and r.room_code = game_room_invites.room_code;
  elsif operation in ('accept','decline') then
    begin selected_id := target::uuid; exception when invalid_text_representation then raise invalid_parameter_value using message = 'Invalid invite'; end;
    if operation = 'accept' then
      select r.room_code, r.room_password into accepted_code, accepted_password
        from public.game_room_invitations i join public.game_invite_rooms r on r.host_id = i.host_id and r.room_code = i.room_code
        where i.id = selected_id and i.recipient_id = me and i.expires_at > now() and r.active_until > now()
        and exists (select 1 from public.game_friendships f where f.low_id = least(me,i.host_id)
          and f.high_id = greatest(me,i.host_id) and f.accepted)
        for update of i, r;
      if not found then raise invalid_parameter_value using message = 'Invite expired or room closed'; end if;
    end if;
    delete from public.game_room_invitations i where i.id = selected_id and i.recipient_id = me;
  end if;
  -- Only a recipient sees their own invites. Passwords never appear in list responses.
  select jsonb_build_object('ok',true,'invites',coalesce(jsonb_agg(jsonb_build_object(
      'id',i.id,'name',p.name,'room_code',i.room_code) order by i.expires_at),'[]'::jsonb)) into result
    from public.game_room_invitations i join public.game_profiles p on p.id = i.host_id
    join public.game_invite_rooms r on r.host_id = i.host_id and r.room_code = i.room_code
    where i.recipient_id = me and i.expires_at > now() and r.active_until > now()
      and exists(select 1 from public.game_friendships f where f.low_id = least(me,i.host_id)
        and f.high_id = greatest(me,i.host_id) and f.accepted);
  -- Recipient-scoped cleanup of expired invitations.
  delete from public.game_room_invitations i where i.recipient_id = me and i.expires_at <= now();
  if accepted_code is not null then
    result := result || jsonb_build_object('room_code',accepted_code,'room_password',accepted_password);
  end if;
  return result;
end;
$$;
revoke all on function public.game_room_invites(text,text,text,text) from public, anon;
grant execute on function public.game_room_invites(text,text,text,text) to authenticated;
notify pgrst, 'reload schema';
commit;
